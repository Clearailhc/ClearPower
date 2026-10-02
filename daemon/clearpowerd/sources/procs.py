"""Estimate per-application power from CPU share x (package power - idle floor)."""
import collections
import time

try:
    import psutil
except ImportError:  # pragma: no cover
    psutil = None


class Procs:
    def __init__(self, interval_s=2, floor_window_s=600):
        self.interval = interval_s
        self._next = 0.0
        self._floor = collections.deque()  # (t, package_w) for sliding min
        self._floor_window = floor_window_s
        self.top = []  # list of (name, est_w, cpu_pct)
        if psutil:
            for p in psutil.process_iter(["pid"]):
                try:
                    p.cpu_percent(None)  # prime
                except Exception:
                    pass

    def _idle_floor(self, now, package_w):
        self._floor.append((now, package_w))
        while self._floor and now - self._floor[0][0] > self._floor_window:
            self._floor.popleft()
        return min(w for _, w in self._floor)

    def maybe_sample(self, package_w, n=3):
        """Attribute (package_w - idle floor) by CPU share.

        Every platform feeds the same arithmetic: package_w is the whole machine's CPU power and
        the divisor is the CPU share, so the units cancel and the split is correct whatever a
        given platform's cpu_percent() is normalised to (psutil uses the whole-machine ratio,
        macOS libproc and the Windows process deltas use a per-core percentage).

        An unknown package power (-1: no RAPL, or the first sample after resume) must never reach
        the floor: the floor is a sliding minimum over ten minutes, so a single bogus entry sticks
        for the whole window and every later budget is computed against it. The sample is skipped
        outright and the interval is left untouched, so the next call tries again.
        """
        now = time.monotonic()
        if psutil is None or now < self._next:
            return self.top
        if package_w < 0:
            return self.top
        self._next = now + self.interval
        floor = self._idle_floor(now, package_w)
        budget = max(package_w - floor, 0.0)
        agg = collections.Counter()
        total = 0.0
        for p in psutil.process_iter(["name"]):
            try:
                c = p.cpu_percent(None)
            except Exception:
                continue
            if c <= 0:
                continue
            total += c
            agg[p.info["name"] or "?"] += c
        top = []
        if total > 0:
            for name, c in agg.most_common(n):
                top.append((name, budget * c / total, c))
        self.top = top
        return top
