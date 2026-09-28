// macOS 27 native charging limit, dynamically loaded to keep macOS 14+ support.
// PowerUISmartChargeClient selectors verified on 27.0 (26A428). No private
// entitlements are requested; failures are returned to the user, never bypassed.
#import <Foundation/Foundation.h>
#import <objc/message.h>
#import <dlfcn.h>
#include "csupport.h"

void *cp_charge_open(void) {
    @autoreleasepool {
        if (!dlopen("/System/Library/PrivateFrameworks/PowerUI.framework/Versions/A/PowerUI", RTLD_NOW)) return NULL;
        Class cls = NSClassFromString(@"PowerUISmartChargeClient");
        if (!cls || ![cls instancesRespondToSelector:NSSelectorFromString(@"initWithClientName:")]) return NULL;
        id client = ((id (*)(id, SEL, id))objc_msgSend)([cls alloc], NSSelectorFromString(@"initWithClientName:"), @"ClearPower");
        for (NSString *name in @[@"isMCLSupported", @"availableChargeLimitsWithError:", @"getMCLLimitWithError:",
                                 @"isMCLCurrentlyEnabled:", @"setMCLLimit:error:", @"disableMCL:"]) {
            if (![client respondsToSelector:NSSelectorFromString(name)]) { return NULL; }
        }
        if (!((BOOL (*)(id, SEL))objc_msgSend)(client, NSSelectorFromString(@"isMCLSupported"))) {
            return NULL;
        }
        return (__bridge_retained void *)client;
    }
}
void cp_charge_close(void *client) { (void)CFBridgingRelease(client); }
static int charge_error(NSError *error, char *message, int size) {
    if (message && size > 0) snprintf(message, size, "%s", error ? error.localizedDescription.UTF8String : "Native charge limit unavailable");
    return -1;
}
int cp_charge_limits(void *client, int *limits, int capacity) {
    @autoreleasepool {
        NSError *error = nil;
        id result = ((id (*)(id, SEL, NSError **))objc_msgSend)((__bridge id)client, NSSelectorFromString(@"availableChargeLimitsWithError:"), &error);
        if (error || ![result isKindOfClass:NSArray.class] || [result count] > capacity) return -1;
        int count = 0;
        for (id number in result) {
            if (![number isKindOfClass:NSNumber.class]) return -1;
            limits[count++] = [number intValue];
        }
        return count;
    }
}
int cp_charge_get(void *client, int *limit, int *enabled, char *message, int size) {
    @autoreleasepool {
        NSError *error = nil;
        *limit = ((unsigned char (*)(id, SEL, NSError **))objc_msgSend)((__bridge id)client, NSSelectorFromString(@"getMCLLimitWithError:"), &error);
        if (error) return charge_error(error, message, size);
        *enabled = ((unsigned long long (*)(id, SEL, NSError **))objc_msgSend)((__bridge id)client, NSSelectorFromString(@"isMCLCurrentlyEnabled:"), &error) != 0;
        return error ? charge_error(error, message, size) : 0;
    }
}
int cp_charge_set(void *client, int limit, char *message, int size) {
    @autoreleasepool {
        NSError *error = nil;
        BOOL ok = ((BOOL (*)(id, SEL, unsigned char, NSError **))objc_msgSend)((__bridge id)client, NSSelectorFromString(@"setMCLLimit:error:"), (unsigned char)limit, &error);
        return ok && !error ? 0 : charge_error(error, message, size);
    }
}
int cp_charge_disable(void *client, char *message, int size) {
    @autoreleasepool {
        NSError *error = nil;
        BOOL ok = ((BOOL (*)(id, SEL, NSError **))objc_msgSend)((__bridge id)client, NSSelectorFromString(@"disableMCL:"), &error);
        return ok && !error ? 0 : charge_error(error, message, size);
    }
}
