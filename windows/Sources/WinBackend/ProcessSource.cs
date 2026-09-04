// Per-process CPU usage from one NtQuerySystemInformation(SystemProcessInformation) call
// (about a millisecond for a few hundred processes; Process.GetProcesses() on .NET Framework
// goes through Perflib and costs far more). The psutil half of sources/procs.py.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClearPower.Win
{
    public sealed class ProcessSource
    {
        private Dictionary<(long pid, long created), long> _last = new Dictionary<(long, long), long>();
        private double _lastNow = -1;
        private IntPtr _buf;
        private int _bufLen;

        /// <summary>(name, cpu percent since the previous call) for every process that used CPU.</summary>
        public List<(string name, double cpuPct)> Usage(double now)
        {
            var res = new List<(string, double)>();
            var cur = new Dictionary<(long, long), long>();
            if (!Query()) return res;
            var elapsed100ns = _lastNow < 0 ? 0 : (now - _lastNow) * 1e7;
            var p = _buf;
            while (true)
            {
                var next = Marshal.ReadInt32(p, 0);
                var create = Marshal.ReadInt64(p, 0x20);
                var user = Marshal.ReadInt64(p, 0x28);
                var kernel = Marshal.ReadInt64(p, 0x30);
                var nameLen = Marshal.ReadInt16(p, 0x38);
                var namePtr = Marshal.ReadIntPtr(p, 0x40);
                var pid = Marshal.ReadInt64(p, 0x50);
                var name = (nameLen > 0 && namePtr != IntPtr.Zero) ? Marshal.PtrToStringUni(namePtr, nameLen / 2) : (pid == 0 ? "Idle" : "?");
                var cpu = user + kernel;
                var key = (pid, create);
                cur[key] = cpu;
                if (pid != 0 && elapsed100ns > 0 && _last.TryGetValue(key, out var prev))
                {
                    var d = cpu - prev;
                    if (d > 0) res.Add((name, 100.0 * d / elapsed100ns));
                }
                if (next == 0) break;
                p += next;
            }
            _last = cur;
            _lastNow = now;
            return res;
        }

        private bool Query()
        {
            if (_buf == IntPtr.Zero)
            {
                _bufLen = 512 * 1024;
                _buf = Marshal.AllocHGlobal(_bufLen);
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var st = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessInformation, _buf, _bufLen, out var need);
                if (st == 0) return true;
                if (st != unchecked((int)0xC0000004)) return false;   // STATUS_INFO_LENGTH_MISMATCH
                Marshal.FreeHGlobal(_buf);
                _bufLen = Math.Max(need + 64 * 1024, _bufLen * 2);
                _buf = Marshal.AllocHGlobal(_bufLen);
            }
            return false;
        }
    }
}
