// Probe: read (and optionally set) built-in display brightness via DisplayServices (private).
// Build: clang -o brightness_probe brightness_probe.c -framework CoreGraphics -framework CoreFoundation
// Usage: ./brightness_probe          -> print brightness of main display
//        ./brightness_probe 0.5      -> set to 0.5, print, restore
#include <CoreGraphics/CoreGraphics.h>
#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

typedef int (*GetB_t)(CGDirectDisplayID, float *);
typedef int (*SetB_t)(CGDirectDisplayID, float);
typedef int (*CanChange_t)(CGDirectDisplayID);

int main(int argc, char **argv) {
    void *h = dlopen("/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_NOW);
    if (!h) { fprintf(stderr, "dlopen: %s\n", dlerror()); return 1; }
    GetB_t get = dlsym(h, "DisplayServicesGetBrightness");
    SetB_t set = dlsym(h, "DisplayServicesSetBrightness");
    CanChange_t can = dlsym(h, "DisplayServicesCanChangeBrightness");
    printf("symbols: get=%p set=%p can=%p\n", get, set, can);
    CGDirectDisplayID ids[8]; uint32_t n = 0;
    CGGetOnlineDisplayList(8, ids, &n);
    for (uint32_t i = 0; i < n; i++) {
        float b = -1; int r = get ? get(ids[i], &b) : -1;
        printf("display %u id=%u builtin=%d asleep=%d canChange=%d get=%d brightness=%.4f\n", i, ids[i], CGDisplayIsBuiltin(ids[i]), CGDisplayIsAsleep(ids[i]), can ? can(ids[i]) : -1, r, b);
        if (argc > 1 && CGDisplayIsBuiltin(ids[i]) && set) {
            float target = atof(argv[1]);
            int rs = set(ids[i], target); usleep(500000);
            float b2 = -1; get(ids[i], &b2);
            printf("  set(%.3f) -> %d, readback %.4f; restoring %.4f\n", target, rs, b2, b);
            set(ids[i], b);
        }
    }
    return 0;
}
