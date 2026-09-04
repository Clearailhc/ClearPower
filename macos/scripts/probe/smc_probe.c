// Probe: read SMC keys relevant to ClearPower (charge control, power, temps, fans).
// Build: clang -o smc_probe smc_probe.c -framework IOKit -framework CoreFoundation
// Usage: ./smc_probe            -> read the known key list
//        ./smc_probe list       -> enumerate every key with type/size
//        ./smc_probe KEY ...    -> read given keys
#include <IOKit/IOKitLib.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>

#define KERNEL_INDEX_SMC 2
#define SMC_CMD_READ_BYTES 5
#define SMC_CMD_READ_KEYINFO 9
#define SMC_CMD_READ_INDEX 8

typedef struct { uint8_t major, minor, build, reserved; uint16_t release; } SMCKeyData_vers_t;
typedef struct { uint16_t version, length; uint32_t cpuPLimit, gpuPLimit, memPLimit; } SMCKeyData_pLimitData_t;
typedef struct { uint32_t dataSize, dataType; uint8_t dataAttributes; } SMCKeyData_keyInfo_t;
typedef struct {
    uint32_t key; SMCKeyData_vers_t vers; SMCKeyData_pLimitData_t pLimitData; SMCKeyData_keyInfo_t keyInfo;
    uint8_t result, status, data8; uint32_t data32; uint8_t bytes[32];
} SMCKeyData_t;

static io_connect_t conn;
static uint32_t str2u32(const char *s) { return ((uint32_t)s[0] << 24) | ((uint32_t)s[1] << 16) | ((uint32_t)s[2] << 8) | (uint32_t)s[3]; }
static void u322str(uint32_t v, char *s) { s[0] = v >> 24; s[1] = v >> 16; s[2] = v >> 8; s[3] = v; s[4] = 0; }

static kern_return_t call(SMCKeyData_t *in, SMCKeyData_t *out) {
    size_t sz = sizeof(SMCKeyData_t);
    return IOConnectCallStructMethod(conn, KERNEL_INDEX_SMC, in, sz, out, &sz);
}

static int read_key(const char *key, SMCKeyData_keyInfo_t *info, uint8_t *bytes) {
    SMCKeyData_t in = {0}, out = {0};
    in.key = str2u32(key); in.data8 = SMC_CMD_READ_KEYINFO;
    if (call(&in, &out) != KERN_SUCCESS || out.result != 0) return -1;
    *info = out.keyInfo;
    memset(&in, 0, sizeof in); in.key = str2u32(key); in.keyInfo.dataSize = info->dataSize; in.data8 = SMC_CMD_READ_BYTES;
    if (call(&in, &out) != KERN_SUCCESS || out.result != 0) return -2;
    memcpy(bytes, out.bytes, 32);
    return 0;
}

static float flt_val(SMCKeyData_keyInfo_t *i, uint8_t *b) {
    char t[5]; u322str(i->dataType, t);
    if (!strcmp(t, "flt ") && i->dataSize == 4) { float f; memcpy(&f, b, 4); return f; }
    if (!strcmp(t, "ui8 ")) return b[0];
    if (!strcmp(t, "ui16")) return (b[0] << 8) | b[1];
    if (!strcmp(t, "ui32")) return (float)(((uint32_t)b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    if (!strcmp(t, "sp78")) return ((int16_t)((b[0] << 8) | b[1])) / 256.0f;
    if (!strcmp(t, "fpe2")) return ((b[0] << 8) | b[1]) / 4.0f;
    return -999;
}

static void show(const char *key) {
    SMCKeyData_keyInfo_t info; uint8_t b[32];
    int r = read_key(key, &info, b);
    if (r) { printf("  %s: (absent, %d)\n", key, r); return; }
    char t[5]; u322str(info.dataType, t);
    printf("  %s: type=%s size=%u val=%.3f bytes=", key, t, info.dataSize, flt_val(&info, b));
    for (uint32_t i = 0; i < info.dataSize && i < 32; i++) printf("%02x", b[i]);
    printf("\n");
}

int main(int argc, char **argv) {
    io_service_t svc = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"));
    if (!svc || IOServiceOpen(svc, mach_task_self(), 0, &conn) != KERN_SUCCESS) { fprintf(stderr, "cannot open AppleSMC\n"); return 1; }
    if (argc > 1 && !strcmp(argv[1], "list")) {
        SMCKeyData_keyInfo_t info; uint8_t b[32];
        if (read_key("#KEY", &info, b)) return 1;
        uint32_t n = ((uint32_t)b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        printf("#KEY = %u\n", n);
        for (uint32_t i = 0; i < n; i++) {
            SMCKeyData_t in = {0}, out = {0};
            in.data8 = SMC_CMD_READ_INDEX; in.data32 = i;
            if (call(&in, &out) != KERN_SUCCESS || out.result != 0) continue;
            char k[5]; u322str(out.key, k);
            SMCKeyData_keyInfo_t ki; uint8_t bb[32];
            if (read_key(k, &ki, bb) == 0) { char t[5]; u322str(ki.dataType, t); printf("%s %s %u %.3f\n", k, t, ki.dataSize, flt_val(&ki, bb)); }
            else printf("%s ?\n", k);
        }
        return 0;
    }
    if (argc > 1) { for (int i = 1; i < argc; i++) show(argv[i]); return 0; }
    printf("charge control:\n"); show("CH0B"); show("CH0C"); show("CH0I"); show("CHWA"); show("CHTE"); show("CHIE"); show("CHLS"); show("BCLM");
    printf("power:\n"); show("PSTR"); show("PDTR"); show("PPBR"); show("PHPC"); show("PMVC");
    printf("fans:\n"); show("FNum"); show("F0Ac"); show("F1Ac"); show("F0Tg"); show("F0Mn"); show("F0Mx");
    printf("battery:\n"); show("TB0T"); show("B0AV"); show("B0AC"); show("BRSC"); show("BUIC");
    return 0;
}
