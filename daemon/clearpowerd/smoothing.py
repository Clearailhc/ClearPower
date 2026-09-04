"""Exponential moving average with a wall-clock time constant."""
import math


class Ema:
    def __init__(self, tau_s):
        self.tau = max(float(tau_s), 0.0)
        self.v = None
        self.t = None

    def update(self, x, t):
        if x is None:
            return self.v
        if self.v is None or self.tau == 0:
            self.v = float(x)
        else:
            a = 1.0 - math.exp(-max(t - self.t, 0.0) / self.tau)
            self.v += a * (float(x) - self.v)
        self.t = t
        return self.v

    def reset(self):
        self.v = None
