"""In-memory ring buffer history, downsampled (mean) to a fixed step."""
import collections

FIELDS = ("sys_w", "soc_w", "bat_w", "bat_pct", "display_w", "temp_cpu", "adapter_w")


class History:
    def __init__(self, seconds=86400, step_s=10):
        self.step = step_s
        n = max(1, seconds // step_s)
        self.buf = {f: collections.deque(maxlen=n) for f in FIELDS}
        self._acc = {f: [0.0, 0] for f in FIELDS}
        self._bucket = None

    def add(self, snap):
        b = int(snap["ts"]) // self.step
        if self._bucket is None:
            self._bucket = b
        if b != self._bucket:
            t = self._bucket * self.step
            for f, (s, c) in self._acc.items():
                if c:
                    self.buf[f].append((t, s / c))
            self._acc = {f: [0.0, 0] for f in FIELDS}
            self._bucket = b
        for f in FIELDS:
            v = snap.get(f)
            if isinstance(v, (int, float)) and v >= -1e9:
                self._acc[f][0] += float(v)
                self._acc[f][1] += 1

    def get(self, field, seconds):
        if field not in self.buf:
            return []
        if not self.buf[field]:
            return []
        t0 = self.buf[field][-1][0] - seconds
        return [(t, v) for t, v in self.buf[field] if t >= t0]
