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
        private double _t0;

        public Ema(double tauS)
        {
            Tau = Math.Max(tauS, 0.0);
        }

        /// <summary>
        /// False until the average has been updated and has had one time constant to converge, so
        /// a caller can tell "an average of real samples" from "the first value I happened to
        /// see". After <see cref="Reset"/> (resume from sleep, gone or unreadable counter) the
        /// ramp starts over and this is false again for one tau.
        /// </summary>
        public bool Settled => V != null && _t - _t0 >= Tau;

        public double Update(double x, double t)
        {
            if (V == null)
            {
                V = x;
                _t0 = t;
            }
            else
            {
                var a = Tau == 0 ? 1.0 : 1.0 - Math.Exp(-Math.Max(t - _t, 0.0) / Tau);
                V += a * (x - V.Value);
            }
            _t = t;
            return V.Value;
        }

        public void Reset()
        {
            V = null;
            _t0 = 0;
        }
    }
}
