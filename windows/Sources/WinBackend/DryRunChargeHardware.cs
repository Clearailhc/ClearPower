// A charge backend that records what would be written instead of writing it.
//
// Two reasons this exists. `--charge --dry-run` needs to show the exact sequence a command would
// send to the embedded controller without touching it, which is the only safe way to ask a user on
// an unmapped vendor to report what the app intends to do. And it makes the write path observable
// in tests: the real backends are thin wrappers over RPC and WMI, so the interesting part is which
// calls they make and in what order.
using System;
using System.Collections.Generic;
using ClearPower.Core;

namespace ClearPower.Win
{
    /// <summary>One operation a backend attempted, in order.</summary>
    public sealed class DryRunEntry
    {
        public string Operation { get; }
        public string Detail { get; }
        public DryRunEntry(string operation, string detail) { Operation = operation; Detail = detail; }
        public override string ToString() => Detail.Length > 0 ? $"{Operation} {Detail}" : Operation;
    }

    public sealed class DryRunChargeHardware : IChargeHardware, IChargeHardwareInfo
    {
        private readonly IChargeHardware _inner;
        private readonly Action<string> _log;
        public List<DryRunEntry> Entries { get; } = new List<DryRunEntry>();

        public DryRunChargeHardware(IChargeHardware inner, Action<string> log)
        {
            _inner = inner;
            _log = log;
        }

        private void Record(string op, string detail)
        {
            var e = new DryRunEntry(op, detail);
            Entries.Add(e);
            _log("would " + e);
        }

        /// <summary>Reported, not performed: the caller must still see the real capability.</summary>
        public bool ThresholdsSupported => _inner.ThresholdsSupported;
        public IReadOnlyList<string> Behaviours => _inner.Behaviours;

        public void WriteThresholds(int start, int end) => Record("write thresholds", $"start={start} end={end}");
        public void WriteBehaviour(string behaviour) => Record("write behaviour", behaviour);

        /// <summary>Reading is safe, so this passes through - that is how the limit is shown.</summary>
        public int? LoadLimit() => _inner.LoadLimit();
        public void SaveLimit(int limit) => Record("save limit", $"{limit}%");

        public Dictionary<string, object?> ExtraState() => _inner is IChargeHardwareInfo i
            ? i.ExtraState()
            : new Dictionary<string, object?>();

        public void Reassert() => Record("re-apply the saved limit", "");
    }
}
