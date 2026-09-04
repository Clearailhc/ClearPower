// Probe: list IOReport "Energy Model" channels and their per-second deltas.
// Build: clang -o ioreport_probe ioreport_probe.c -framework CoreFoundation
#include <CoreFoundation/CoreFoundation.h>
#include <dlfcn.h>
#include <stdio.h>
#include <unistd.h>

typedef CFDictionaryRef (*CopyChannelsInGroup_t)(CFStringRef, CFStringRef, uint64_t, uint64_t, uint64_t);
typedef void *(*CreateSubscription_t)(void *, CFMutableDictionaryRef, CFMutableDictionaryRef *, uint64_t, CFTypeRef);
typedef CFDictionaryRef (*CreateSamples_t)(void *, CFMutableDictionaryRef, CFTypeRef);
typedef CFDictionaryRef (*CreateSamplesDelta_t)(CFDictionaryRef, CFDictionaryRef, CFTypeRef);
typedef CFStringRef (*GetStr_t)(CFDictionaryRef);
typedef int64_t (*GetInt_t)(CFDictionaryRef, int32_t);

static void cstr(CFStringRef s, char *buf, size_t n) { buf[0] = 0; if (s) CFStringGetCString(s, buf, n, kCFStringEncodingUTF8); }

int main(int argc, char **argv) {
    const char *group = argc > 1 ? argv[1] : "Energy Model";
    void *h = dlopen("/usr/lib/libIOReport.dylib", RTLD_NOW);
    if (!h) { fprintf(stderr, "dlopen failed: %s\n", dlerror()); return 1; }
    CopyChannelsInGroup_t copyGroup = dlsym(h, "IOReportCopyChannelsInGroup");
    CreateSubscription_t createSub = dlsym(h, "IOReportCreateSubscription");
    CreateSamples_t createSamples = dlsym(h, "IOReportCreateSamples");
    CreateSamplesDelta_t createDelta = dlsym(h, "IOReportCreateSamplesDelta");
    GetStr_t getGroup = dlsym(h, "IOReportChannelGetGroup");
    GetStr_t getSub = dlsym(h, "IOReportChannelGetSubGroup");
    GetStr_t getName = dlsym(h, "IOReportChannelGetChannelName");
    GetStr_t getUnit = dlsym(h, "IOReportChannelGetUnitLabel");
    GetInt_t getInt = dlsym(h, "IOReportSimpleGetIntegerValue");
    if (!copyGroup || !createSub || !createSamples || !createDelta || !getName || !getInt) { fprintf(stderr, "missing symbols\n"); return 1; }

    CFStringRef g = CFStringCreateWithCString(NULL, group, kCFStringEncodingUTF8);
    CFDictionaryRef chan = copyGroup(g, NULL, 0, 0, 0);
    if (!chan) { fprintf(stderr, "no channels for group %s\n", group); return 1; }
    CFMutableDictionaryRef desired = CFDictionaryCreateMutableCopy(NULL, 0, chan);
    CFMutableDictionaryRef subbed = NULL;
    void *sub = createSub(NULL, desired, &subbed, 0, NULL);
    if (!sub) { fprintf(stderr, "subscription failed\n"); return 1; }

    CFDictionaryRef s1 = createSamples(sub, subbed, NULL);
    usleep(1000 * 1000);
    CFDictionaryRef s2 = createSamples(sub, subbed, NULL);
    CFDictionaryRef delta = createDelta(s1, s2, NULL);
    CFArrayRef arr = CFDictionaryGetValue(delta, CFSTR("IOReportChannels"));
    long n = arr ? CFArrayGetCount(arr) : 0;
    printf("group=%s channels=%ld (1 s delta)\n", group, n);
    for (long i = 0; i < n; i++) {
        CFDictionaryRef c = CFArrayGetValueAtIndex(arr, i);
        char gs[64], ss[64], ns[128], us[32];
        cstr(getGroup(c), gs, 64); cstr(getSub(c), ss, 64); cstr(getName(c), ns, 128); cstr(getUnit ? getUnit(c) : NULL, us, 32);
        printf("  [%s / %s] %-32s = %lld %s\n", gs, ss, ns, (long long)getInt(c, 0), us);
    }
    return 0;
}
