#include "csupport.h"
#include <CoreFoundation/CoreFoundation.h>
#include <CoreGraphics/CoreGraphics.h>
#include <IOKit/IOKitLib.h>
#include <dlfcn.h>
#include <mach/mach_time.h>
#include <string.h>
#include <stdlib.h>

// ============================== SMC ==============================
#define KERNEL_INDEX_SMC 2
#define SMC_CMD_READ_BYTES 5
#define SMC_CMD_WRITE_BYTES 6
#define SMC_CMD_READ_INDEX 8
#define SMC_CMD_READ_KEYINFO 9

typedef struct { uint8_t major, minor, build, reserved; uint16_t release; } SMCKeyData_vers_t;
typedef struct { uint16_t version, length; uint32_t cpuPLimit, gpuPLimit, memPLimit; } SMCKeyData_pLimitData_t;
typedef struct { uint32_t dataSize, dataType; uint8_t dataAttributes; } SMCKeyData_keyInfo_t;
typedef struct {
    uint32_t key; SMCKeyData_vers_t vers; SMCKeyData_pLimitData_t pLimitData; SMCKeyData_keyInfo_t keyInfo;
    uint8_t result, status, data8; uint32_t data32; uint8_t bytes[32];
} SMCKeyData_t;

static io_connect_t g_conn = 0;

static uint32_t str2u32(const char *s) {
    return ((uint32_t)(uint8_t)s[0] << 24) | ((uint32_t)(uint8_t)s[1] << 16) | ((uint32_t)(uint8_t)s[2] << 8) | (uint32_t)(uint8_t)s[3];
}
static void u322str(uint32_t v, char *s) { s[0] = v >> 24; s[1] = v >> 16; s[2] = v >> 8; s[3] = v; s[4] = 0; }

static kern_return_t smc_call(SMCKeyData_t *in, SMCKeyData_t *out) {
    size_t sz = sizeof(SMCKeyData_t);
    return IOConnectCallStructMethod(g_conn, KERNEL_INDEX_SMC, in, sz, out, &sz);
}

int cp_smc_open(void) {
    if (g_conn) return 0;
    io_service_t svc = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"));
    if (!svc) return -1;
    kern_return_t kr = IOServiceOpen(svc, mach_task_self(), 0, &g_conn);
    IOObjectRelease(svc);
    return kr == KERN_SUCCESS ? 0 : -1;
}

void cp_smc_close(void) {
    if (g_conn) { IOServiceClose(g_conn); g_conn = 0; }
}

static int smc_keyinfo(const char *key, SMCKeyData_keyInfo_t *info) {
    SMCKeyData_t in = {0}, out = {0};
    in.key = str2u32(key); in.data8 = SMC_CMD_READ_KEYINFO;
    if (smc_call(&in, &out) != KERN_SUCCESS || out.result != 0) return -1;
    *info = out.keyInfo;
    return 0;
}

int cp_smc_read(const char *key, cp_smc_value *val) {
    if (!g_conn && cp_smc_open()) return -2;
    SMCKeyData_keyInfo_t info;
    if (smc_keyinfo(key, &info)) return -1;
    SMCKeyData_t in = {0}, out = {0};
    in.key = str2u32(key); in.keyInfo.dataSize = info.dataSize; in.data8 = SMC_CMD_READ_BYTES;
    if (smc_call(&in, &out) != KERN_SUCCESS || out.result != 0) return -2;
    val->size = info.dataSize > 32 ? 32 : info.dataSize;
    u322str(info.dataType, val->type);
    memcpy(val->bytes, out.bytes, 32);
    return 0;
}

int cp_smc_write(const char *key, const uint8_t *bytes, uint32_t size) {
    if (!g_conn && cp_smc_open()) return -2;
    SMCKeyData_keyInfo_t info;
    if (smc_keyinfo(key, &info)) return -1;
    if (size > 32 || size != info.dataSize) return -3;
    SMCKeyData_t in = {0}, out = {0};
    in.key = str2u32(key); in.keyInfo.dataSize = size; in.data8 = SMC_CMD_WRITE_BYTES;
    memcpy(in.bytes, bytes, size);
    if (smc_call(&in, &out) != KERN_SUCCESS || out.result != 0) return -2;
    return 0;
}

int cp_smc_key_count(void) {
    cp_smc_value v;
    if (cp_smc_read("#KEY", &v) || v.size != 4) return -1;
    return (int)(((uint32_t)v.bytes[0] << 24) | (v.bytes[1] << 16) | (v.bytes[2] << 8) | v.bytes[3]);
}

int cp_smc_key_at(uint32_t index, char out[5]) {
    if (!g_conn && cp_smc_open()) return -2;
    SMCKeyData_t in = {0}, o = {0};
    in.data8 = SMC_CMD_READ_INDEX; in.data32 = index;
    if (smc_call(&in, &o) != KERN_SUCCESS || o.result != 0) return -1;
    u322str(o.key, out);
    return 0;
}

// ============================== IOReport ==============================
typedef CFDictionaryRef (*CopyChannelsInGroup_t)(CFStringRef, CFStringRef, uint64_t, uint64_t, uint64_t);
typedef void *(*CreateSubscription_t)(void *, CFMutableDictionaryRef, CFMutableDictionaryRef *, uint64_t, CFTypeRef);
typedef CFDictionaryRef (*CreateSamples_t)(void *, CFMutableDictionaryRef, CFTypeRef);
typedef CFDictionaryRef (*CreateSamplesDelta_t)(CFDictionaryRef, CFDictionaryRef, CFTypeRef);
typedef CFStringRef (*GetStr_t)(CFDictionaryRef);
typedef int64_t (*GetInt_t)(CFDictionaryRef, int32_t);

struct cp_ioreport {
    void *sub;
    CFMutableDictionaryRef subbed;
    CFDictionaryRef prev;
    uint64_t prev_t;
    CreateSamples_t createSamples;
    CreateSamplesDelta_t createDelta;
    GetStr_t getName, getUnit;
    GetInt_t getInt;
};

static double mach_to_seconds(uint64_t dt) {
    static mach_timebase_info_data_t tb;
    if (tb.denom == 0) mach_timebase_info(&tb);
    return (double)dt * tb.numer / tb.denom / 1e9;
}

cp_ioreport *cp_ioreport_open(const char *group) {
    void *h = dlopen("/usr/lib/libIOReport.dylib", RTLD_NOW);
    if (!h) return NULL;
    CopyChannelsInGroup_t copyGroup = dlsym(h, "IOReportCopyChannelsInGroup");
    CreateSubscription_t createSub = dlsym(h, "IOReportCreateSubscription");
    cp_ioreport *r = calloc(1, sizeof *r);
    r->createSamples = dlsym(h, "IOReportCreateSamples");
    r->createDelta = dlsym(h, "IOReportCreateSamplesDelta");
    r->getName = dlsym(h, "IOReportChannelGetChannelName");
    r->getUnit = dlsym(h, "IOReportChannelGetUnitLabel");
    r->getInt = dlsym(h, "IOReportSimpleGetIntegerValue");
    if (!copyGroup || !createSub || !r->createSamples || !r->createDelta || !r->getName || !r->getInt) { free(r); return NULL; }
    CFStringRef g = CFStringCreateWithCString(NULL, group, kCFStringEncodingUTF8);
    CFDictionaryRef chan = copyGroup(g, NULL, 0, 0, 0);
    CFRelease(g);
    if (!chan) { free(r); return NULL; }
    CFMutableDictionaryRef desired = CFDictionaryCreateMutableCopy(NULL, 0, chan);
    CFRelease(chan);
    r->sub = createSub(NULL, desired, &r->subbed, 0, NULL);
    CFRelease(desired);
    if (!r->sub) { free(r); return NULL; }
    return r;
}

void cp_ioreport_close(cp_ioreport *r) {
    if (!r) return;
    if (r->prev) CFRelease(r->prev);
    if (r->subbed) CFRelease(r->subbed);
    if (r->sub) CFRelease(r->sub);
    free(r);
}

int cp_ioreport_sample(cp_ioreport *r, cp_energy_entry *out, int max_entries, double *elapsed_s) {
    if (!r) return 0;
    CFDictionaryRef cur = r->createSamples(r->sub, r->subbed, NULL);
    uint64_t now = mach_absolute_time();
    if (!cur) return 0;
    if (r->prev && mach_to_seconds(now - r->prev_t) < 0.05) {
        // Too soon for a meaningful delta: keep the previous baseline, report "no change".
        CFRelease(cur);
        if (elapsed_s) *elapsed_s = 0;
        return -1;
    }
    int n = 0;
    if (r->prev) {
        CFDictionaryRef delta = r->createDelta(r->prev, cur, NULL);
        if (elapsed_s) *elapsed_s = mach_to_seconds(now - r->prev_t);
        CFArrayRef arr = delta ? CFDictionaryGetValue(delta, CFSTR("IOReportChannels")) : NULL;
        long cnt = arr ? CFArrayGetCount(arr) : 0;
        for (long i = 0; i < cnt && n < max_entries; i++) {
            CFDictionaryRef c = CFArrayGetValueAtIndex(arr, i);
            CFStringRef name = r->getName(c);
            if (!name) continue;
            char unit[16] = {0};
            CFStringRef u = r->getUnit ? r->getUnit(c) : NULL;
            if (u) CFStringGetCString(u, unit, sizeof unit, kCFStringEncodingUTF8);
            double scale = 1.0;
            if (!strcmp(unit, "mJ")) scale = 1e-3;
            else if (!strcmp(unit, "uJ")) scale = 1e-6;
            else if (!strcmp(unit, "nJ")) scale = 1e-9;
            else if (!strcmp(unit, "J")) scale = 1.0;
            else continue;  // not an energy channel
            CFStringGetCString(name, out[n].name, sizeof out[n].name, kCFStringEncodingUTF8);
            out[n].joules = (double)r->getInt(c, 0) * scale;
            n++;
        }
        if (delta) CFRelease(delta);
        CFRelease(r->prev);
    } else if (elapsed_s) {
        *elapsed_s = 0;
    }
    r->prev = cur;
    r->prev_t = now;
    return n;
}

// ============================== Display ==============================
typedef int (*DSGet_t)(CGDirectDisplayID, float *);
typedef int (*DSSet_t)(CGDirectDisplayID, float);
static DSGet_t ds_get; static DSSet_t ds_set; static int ds_loaded;

static void ds_load(void) {
    if (ds_loaded) return;
    ds_loaded = 1;
    void *h = dlopen("/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_NOW);
    if (!h) return;
    ds_get = dlsym(h, "DisplayServicesGetBrightness");
    ds_set = dlsym(h, "DisplayServicesSetBrightness");
}

static CGDirectDisplayID builtin_display(void) {
    CGDirectDisplayID ids[16]; uint32_t n = 0;
    if (CGGetOnlineDisplayList(16, ids, &n) != kCGErrorSuccess) return 0;
    for (uint32_t i = 0; i < n; i++) if (CGDisplayIsBuiltin(ids[i])) return ids[i];
    return n ? ids[0] : 0;
}

int cp_brightness_get(float *out) {
    ds_load();
    CGDirectDisplayID d = builtin_display();
    if (!ds_get || !d) return -1;
    return ds_get(d, out) == 0 ? 0 : -1;
}

int cp_brightness_set(float value) {
    ds_load();
    CGDirectDisplayID d = builtin_display();
    if (!ds_set || !d) return -1;
    if (value < 0) value = 0; if (value > 1) value = 1;
    return ds_set(d, value) == 0 ? 0 : -1;
}

int cp_display_asleep(void) {
    CGDirectDisplayID d = builtin_display();
    if (!d) return -1;
    return CGDisplayIsAsleep(d) ? 1 : 0;
}
