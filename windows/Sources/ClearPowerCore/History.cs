// In-memory ring buffer history, downsampled (mean) to a fixed step.
// Port of daemon/clearpowerd/history.py.
using System;
using System.Collections.Generic;

namespace ClearPower.Core
{
    public sealed class History
    {
        public static readonly string[] Fields = { "sys_w", "soc_w", "bat_w", "bat_pct", "display_w", "temp_cpu", "adapter_w" };

        private readonly int _step;
        private readonly int _n;
        private readonly Dictionary<string, LinkedList<(double t, double v)>> _buf = new Dictionary<string, LinkedList<(double, double)>>();
        private Dictionary<string, (double sum, int count)> _acc = new Dictionary<string, (double, int)>();
        private long? _bucket;

        public History(int seconds = 86400, int stepS = 10)
        {
            _step = stepS;
            _n = Math.Max(1, seconds / stepS);
            foreach (var f in Fields)
            {
                _buf[f] = new LinkedList<(double, double)>();
                _acc[f] = (0.0, 0);
            }
        }

        public void Add(IDictionary<string, object?> snap)
        {
            var b = (long)Math.Floor(snap.D("ts", 0)) / _step;
            if (_bucket == null) _bucket = b;
            if (b != _bucket)
            {
                var t = (double)(_bucket.Value * _step);
                foreach (var f in Fields)
                {
                    var (s, c) = _acc[f];
                    if (c > 0)
                    {
                        _buf[f].AddLast((t, s / c));
                        while (_buf[f].Count > _n) _buf[f].RemoveFirst();
                    }
                }
                _acc = new Dictionary<string, (double, int)>();
                foreach (var f in Fields) _acc[f] = (0.0, 0);
                _bucket = b;
            }
            foreach (var f in Fields)
            {
                if (!snap.TryGetValue(f, out var raw)) continue;
                if (raw is bool) continue;
                var v = Snapshot.Double(raw);
                if (v == null || v < -1e9) continue;
                var (s, c) = _acc[f];
                _acc[f] = (s + v.Value, c + 1);
            }
        }

        public List<(double t, double v)> Get(string field, double seconds)
        {
            var res = new List<(double, double)>();
            if (!_buf.TryGetValue(field, out var q) || q.Count == 0) return res;
            var t0 = q.Last!.Value.t - seconds;
            foreach (var (t, v) in q)
                if (t >= t0) res.Add((t, v));
            return res;
        }
    }
}
