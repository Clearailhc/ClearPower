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

    #: The idle floor is this percentile of the package power over the window, not its minimum.
    #: A minimum is pinned by whichever single low reading happened to occur and can never rise
    #: again: on a machine that never goes properly idle it is the lowest sample of ten minutes,
    #: which can sit well under what "idle plus the usual background" actually costs. A low
    #: percentile tracks the quiet end of the distribution instead of one outlier, so ordinary
    #: background load does not distort it. With 20 % of the samples below it, twenty seconds of
    #: sustained full load cannot move it.
    FLOOR_PERCENTILE = 20

    def _idle_floor(self, now, package_w):
        """Record a reading and return the floor, or None when nothing has been recorded yet."""
        self._floor.append((now, package_w))
        while self._floor and now - self._floor[0][0] > self._floor_window:
            self._floor.popleft()
        if not self._floor:
            return None
        ordered = sorted(w for _, w in self._floor)
        return ordered[min(len(ordered) - 1, len(ordered) * self.FLOOR_PERCENTILE // 100)]

    def maybe_sample(self, package_w, n=3):
        """Attribute (package_w - idle floor) by CPU share.

        Every platform feeds the same arithmetic: package_w is the whole machine's CPU power and
        the divisor is the CPU share, so the units cancel and the split is correct whatever a
        given platform's cpu_percent() is normalised to (psutil uses the whole-machine ratio,
        macOS libproc and the Windows process deltas use a per-core percentage).

        An unknown package power (-1: no RAPL, or the first sample after resume) must never reach
        the floor: recording -1 as 0 W would drag the floor to nothing and turn every later budget
        into the full package power. Such a sample is skipped and the interval is left untouched,
        so the next call tries again.
        """
        now = time.monotonic()
        if psutil is None or now < self._next:
            return self.top
        if package_w < 0:
            return self.top
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
        floor = self._idle_floor(now, package_w)
        if floor is None:
            return self.top                     # nothing recorded yet
        self._next = now + self.interval
        budget = max(package_w - floor, 0.0)
        top = []
        if total > 0:
            for name, c in agg.most_common(n):
                top.append((name, budget * c / total, c))
        self.top = top
        return top
