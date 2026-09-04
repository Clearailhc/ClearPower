"""Battery runtime / time-to-limit estimates from the battery's own energy counter.

A window of W seconds is evaluated over the current uninterrupted discharging (or
charging) segment: avg_w = dE/dt.  This integrates everything the machine drew,
is immune to power_now's slow EC updates, and only moves as fast as the window.
"""
import collections

WINDOWS = (600, 1800, 3600)
MIN_BASIS_S = 60


class Runtime:
    def __init__(self):
        self.buf = collections.deque(maxlen=3700)  # (t, energy_wh, phase)

    @staticmethod
    def _phase(status):
        if status == "Discharging":
            return "dis"
        if status == "Charging":
            return "chg"
        return "idle"

    def add(self, t, energy_wh, status):
        self.buf.append((t, energy_wh, self._phase(status)))

    def _oldest_in_segment(self, window_s):
        """Oldest sample within window_s that shares the newest sample's phase, contiguously."""
        if not self.buf:
            return None
        t_now, _, phase = self.buf[-1]
        oldest = None
        for t, e, p in reversed(self.buf):
            if p != phase or t < t_now - window_s:
                break
            oldest = (t, e)
        return oldest

    def estimate(self, energy_now_wh, target_wh, fallback_w):
        """Returns snapshot keys runtime_min_*, eta_min_*, runtime_basis_s."""
        out = {"runtime_basis_s": 0}
        if not self.buf:
            for w in WINDOWS:
                out[f"runtime_min_{w // 60}"] = -1.0
                out[f"eta_min_{w // 60}"] = -1.0
            return out
        t_now, e_now, phase = self.buf[-1]
        e_now = energy_now_wh or e_now
        for w in WINDOWS:
            key = w // 60
            runtime = eta = -1.0
            old = self._oldest_in_segment(w)
            avg_w = None
            basis = 0
            if old is not None:
                dt = t_now - old[0]
                de = old[1] - e_now  # positive when discharging
                if dt >= MIN_BASIS_S and abs(de) > 1e-3:
                    avg_w = de / dt * 3600.0
                    basis = int(dt)
            if phase == "dis":
                p = avg_w if (avg_w and avg_w > 0.3) else (fallback_w if fallback_w > 0.3 else None)
                if p:
                    runtime = e_now / p * 60.0
            elif phase == "chg":
                p = -avg_w if (avg_w and avg_w < -0.3) else (fallback_w if fallback_w > 0.3 else None)
                if p and target_wh > e_now:
                    eta = (target_wh - e_now) / p * 60.0
            out[f"runtime_min_{key}"] = runtime
            out[f"eta_min_{key}"] = eta
            out["runtime_basis_s"] = max(out["runtime_basis_s"], basis if (runtime > 0 or eta > 0) and avg_w else 0)
        return out
