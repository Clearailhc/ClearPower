// Monotonic seconds that do not advance while the machine sleeps: the Windows equivalent
// of Linux CLOCK_MONOTONIC / Python time.monotonic() used by the daemon.
using System;
using System.Diagnostics;

namespace ClearPower.Win
{
    public static class Clock
    {
        public static double MonotonicNow()
        {
            if (NativeMethods.QueryUnbiasedInterruptTime(out var t)) return t / 1e7;
            return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        }

        public static double UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}
