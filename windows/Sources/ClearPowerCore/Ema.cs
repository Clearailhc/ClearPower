// Exponential moving average with a wall-clock time constant.
// Port of daemon/clearpowerd/smoothing.py.
using System;

namespace ClearPower.Core
{
    public sealed class Ema
    {
        public double Tau { get; }
        public double? V { get; private set; }
        private double _t;

        public Ema(double tauS)
        {
            Tau = Math.Max(tauS, 0.0);
        }

        public double Update(double x, double t)
        {
            if (V == null || Tau == 0)
            {
                V = x;
            }
            else
            {
                var a = 1.0 - Math.Exp(-Math.Max(t - _t, 0.0) / Tau);
                V += a * (x - V.Value);
            }
            _t = t;
            return V.Value;
        }

        public void Reset() => V = null;
    }
}
