// C shims for system interfaces that are awkward from Swift: the AppleSMC user client,
// the private IOReport library and DisplayServices brightness.
#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ---- SMC ----------------------------------------------------------------
typedef struct {
    uint32_t size;
    char type[5];      // e.g. "flt ", "ui8 ", "si16", "hex_"
    uint8_t bytes[32];
} cp_smc_value;

int cp_smc_open(void);                                  // 0 = ok
void cp_smc_close(void);
int cp_smc_read(const char *key, cp_smc_value *out);    // 0 ok, -1 absent, -2 read failed
int cp_smc_write(const char *key, const uint8_t *bytes, uint32_t size);  // 0 ok (root only)
int cp_smc_key_count(void);                             // -1 on error
int cp_smc_key_at(uint32_t index, char out[5]);         // 0 ok

// ---- IOReport -------------------------------------------------------------
typedef struct cp_ioreport cp_ioreport;
typedef struct {
    char name[64];
    double joules;     // energy in the sampling interval, unit-normalised
} cp_energy_entry;

cp_ioreport *cp_ioreport_open(const char *group);       // NULL if unavailable
void cp_ioreport_close(cp_ioreport *r);
// Takes a sample and returns the delta against the previous one (first call: 0 entries).
// Returns the number of entries written, or -1 when called again within 50 ms (baseline
// kept; reuse the previous result). elapsed_s receives the wall-clock interval.
int cp_ioreport_sample(cp_ioreport *r, cp_energy_entry *out, int max_entries, double *elapsed_s);

// ---- Display --------------------------------------------------------------
int cp_brightness_get(float *out);                     // 0 ok (built-in display)
int cp_brightness_set(float value);                    // 0 ok
int cp_display_asleep(void);                           // 1 asleep, 0 awake, -1 no built-in display

#ifdef __cplusplus
}
#endif
