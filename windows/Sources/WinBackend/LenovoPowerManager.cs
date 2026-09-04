// Charge thresholds through Lenovo Power Manager's local RPC server (ncalrpc endpoint
// "BaseModuleRpcEndpoint_0", interface LenPowCtl af8abfc6-2132-4870-bf8d-bca541ffcf0b v1.0).
// This is the same interface Lenovo's own tools use; it is reachable from a normal user
// session, so no service, driver or administrator prompt is needed. The interface was
// documented by the MIT-licensed alandau/LenPwrCtl project; the NDR format strings it
// generated with MIDL are embedded as resources (lenpwr_proc.bin / lenpwr_type.bin) and
// the calls go through rpcrt4's NdrClientCall2 interpreter, so no native stub is shipped.
//
// The EC keeps the thresholds (they survive reboots and other operating systems), so this
// backend behaves like the Linux sysfs thresholds: charging stops at `end` and resumes
// below `start`. Force-discharge has no public Windows interface: behaviours = ["auto"].
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClearPower.Core;
using static ClearPower.Win.NativeMethods;

namespace ClearPower.Win
{
    public sealed class LenovoPowerManager : IChargeHardware, IChargeHardwareInfo, IDisposable
    {
        public const string Endpoint = "BaseModuleRpcEndpoint_0";
        private static readonly Guid InterfaceId = new Guid("af8abfc6-2132-4870-bf8d-bca541ffcf0b");
        private static readonly Guid NdrTransferSyntax = new Guid("8A885D04-1CEB-11C9-9FE8-08002B104860");

        private readonly object _gate = new object();
        private readonly Action<string> _log;
        private IntPtr _binding;
        private IntPtr _ctx;              // NDR client context handle from LpcCreateContext
        private readonly IntPtr _stubDesc; // unmanaged MIDL_STUB_DESC
        private readonly IntPtr _procFmt;  // pinned procedure format string
        private readonly int _battery = 1; // EC battery numbering starts at 1
        private int _start = -1, _stop = -1, _capable = -1, _enabled = -1;
        private double _readAt = -1e9;
        private readonly List<IDisposable> _keep = new List<IDisposable>();

        public bool ThresholdsSupported => _capable > 0;
        public IReadOnlyList<string> Behaviours => new[] { "auto" };

        // ---- NDR plumbing --------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct RPC_SYNTAX_IDENTIFIER
        {
            public Guid SyntaxGUID;
            public ushort MajorVersion;
            public ushort MinorVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RPC_CLIENT_INTERFACE
        {
            public uint Length;
            public RPC_SYNTAX_IDENTIFIER InterfaceId;
            public RPC_SYNTAX_IDENTIFIER TransferSyntax;
            public IntPtr DispatchTable;
            public uint RpcProtseqEndpointCount;
            public IntPtr RpcProtseqEndpoint;
            public IntPtr DefaultManagerEpv;
            public IntPtr InterpreterInfo;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIDL_STUB_DESC
        {
            public IntPtr RpcInterfaceInformation;
            public IntPtr pfnAllocate;
            public IntPtr pfnFree;
            public IntPtr pAutoHandle;
            public IntPtr apfnNdrRundownRoutines;
            public IntPtr aGenericBindingRoutinePairs;
            public IntPtr apfnExprEval;
            public IntPtr aXmitQuintuple;
            public IntPtr pFormatTypes;
            public int fCheckBounds;
            public uint Version;
            public IntPtr pMallocFreeStruct;
            public int MIDLVersion;
            public IntPtr CommFaultOffsets;
            public IntPtr aUserMarshalQuadruple;
            public IntPtr NotifyRoutineTable;
            public IntPtr mFlags;
            public IntPtr CsRoutineTables;
            public IntPtr ProxyServerInfo;
            public IntPtr pExprInfo;
        }

        private sealed class Pinned : IDisposable
        {
            public GCHandle Handle;
            public IntPtr Ptr => Handle.AddrOfPinnedObject();
            public Pinned(object o) { Handle = GCHandle.Alloc(o, GCHandleType.Pinned); }
            public void Dispose() { if (Handle.IsAllocated) Handle.Free(); }
        }

        private static readonly MidlUserAllocate AllocDelegate = size => Marshal.AllocHGlobal(size);
        private static readonly MidlUserFree FreeDelegate = ptr => Marshal.FreeHGlobal(ptr);

        private static byte[] Resource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var n in asm.GetManifestResourceNames())
            {
                if (!n.EndsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                using var s = asm.GetManifestResourceStream(n)!;
                var b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                return b;
            }
            throw new FileNotFoundException(name);
        }

        private LenovoPowerManager(Action<string> log)
        {
            _log = log;
            var proc = new Pinned(Resource("lenpwr_proc.bin"));
            var type = new Pinned(Resource("lenpwr_type.bin"));
            _keep.Add(proc); _keep.Add(type);
            _procFmt = proc.Ptr;

            var iface = new RPC_CLIENT_INTERFACE
            {
                Length = (uint)Marshal.SizeOf<RPC_CLIENT_INTERFACE>(),
                InterfaceId = new RPC_SYNTAX_IDENTIFIER { SyntaxGUID = InterfaceId, MajorVersion = 1, MinorVersion = 0 },
                TransferSyntax = new RPC_SYNTAX_IDENTIFIER { SyntaxGUID = NdrTransferSyntax, MajorVersion = 2, MinorVersion = 0 },
            };
            var pIface = Marshal.AllocHGlobal(Marshal.SizeOf<RPC_CLIENT_INTERFACE>());
            Marshal.StructureToPtr(iface, pIface, false);
            var pAuto = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(pAuto, IntPtr.Zero);
            var desc = new MIDL_STUB_DESC
            {
                RpcInterfaceInformation = pIface,
                pfnAllocate = Marshal.GetFunctionPointerForDelegate(AllocDelegate),
                pfnFree = Marshal.GetFunctionPointerForDelegate(FreeDelegate),
                pAutoHandle = pAuto,
                pFormatTypes = type.Ptr,
                fCheckBounds = 1,
                Version = 0x60001,      // NDR library version
                MIDLVersion = 0x801026e,
                mFlags = new IntPtr(1),
            };
            _stubDesc = Marshal.AllocHGlobal(Marshal.SizeOf<MIDL_STUB_DESC>());
            Marshal.StructureToPtr(desc, _stubDesc, false);
        }

        /// <summary>Returns null when Lenovo Power Manager is not running on this machine.</summary>
        public static LenovoPowerManager? TryCreate(Action<string> log)
        {
            try
            {
                var pm = new LenovoPowerManager(log);
                if (!pm.Connect()) { pm.Dispose(); return null; }
                pm.ReadThresholds(force: true);
                log($"charge control: Lenovo Power Manager RPC (capable={pm._capable}, enabled={pm._enabled}, start={pm._start}, stop={pm._stop})");
                if (pm._capable <= 0) { pm.Dispose(); return null; }
                return pm;
            }
            catch (Exception e)
            {
                log($"charge control: Lenovo Power Manager unavailable: {e.Message}");
                return null;
            }
        }

        private bool Connect()
        {
            lock (_gate)
            {
                if (_ctx != IntPtr.Zero) return true;
                if (_binding == IntPtr.Zero)
                {
                    var r = RpcStringBindingComposeW(null, "ncalrpc", null, Endpoint, null, out var sb);
                    if (r != 0) return false;
                    r = RpcBindingFromStringBindingW(sb, out _binding);
                    RpcStringFreeW(ref sb);
                    if (r != 0) { _binding = IntPtr.Zero; return false; }
                }
                if (RpcMgmtIsServerListening(_binding) != 0)
                {
                    _log("charge control: Lenovo Power Manager endpoint not listening");
                    return false;
                }
                var pCtx = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(pCtx, IntPtr.Zero);
                    NdrClientCall2(_stubDesc, _procFmt + LenovoRpcFormat.ProcCreateContext, __arglist(_binding, pCtx));
                    _ctx = Marshal.ReadIntPtr(pCtx);
                }
                catch (Exception e)
                {
                    _log($"charge control: LpcCreateContext failed: {Describe(e)}");
                    _ctx = IntPtr.Zero;
                    return false;
                }
                finally { Marshal.FreeHGlobal(pCtx); }
                return _ctx != IntPtr.Zero;
            }
        }

        private static string Describe(Exception e) => e is SEHException seh ? $"RPC error 0x{seh.ErrorCode:x8} / {seh.Message}" : e.Message;

        private void DropContext()
        {
            _ctx = IntPtr.Zero;   // the server side is torn down when the binding dies; nothing to free locally
        }

        /// <summary>LpcGetChargeThreshold: (capable, enabled, start, stop) for the battery.</summary>
        private (short capable, short enabled, int start, int stop) GetChargeThreshold()
        {
            var mem = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < 32; i++) Marshal.WriteByte(mem, i, 0);
                NdrClientCall2(_stubDesc, _procFmt + LenovoRpcFormat.ProcGetChargeThreshold,
                    __arglist(_ctx, _battery, mem, mem + 8, mem + 16, mem + 24));
                return (Marshal.ReadInt16(mem), Marshal.ReadInt16(mem + 8), Marshal.ReadInt32(mem + 16), Marshal.ReadInt32(mem + 24));
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private void SetChargeThreshold(int start, int stop)
        {
            NdrClientCall2(_stubDesc, _procFmt + LenovoRpcFormat.ProcSetChargeThreshold, __arglist(_ctx, _battery, start, stop));
        }

        private void ReadThresholds(bool force)
        {
            var now = Clock.MonotonicNow();
            if (!force && now - _readAt < 30) return;
            lock (_gate)
            {
                if (!Connect()) return;
                try
                {
                    var (cap, en, st, sp) = GetChargeThreshold();
                    _capable = cap; _enabled = en; _start = st; _stop = sp;
                    _readAt = now;
                }
                catch (Exception e)
                {
                    _log($"charge control: LpcGetChargeThreshold failed: {Describe(e)}");
                    DropContext();
                }
            }
        }

        // ---- IChargeHardware ---------------------------------------------------------------

        public void WriteThresholds(int start, int end)
        {
            lock (_gate)
            {
                if (!Connect()) throw new ChargeException(5, "Lenovo Power Manager not reachable");
                try
                {
                    SetChargeThreshold(start, end);
                }
                catch (Exception e)
                {
                    DropContext();
                    throw new ChargeException(5, $"set threshold failed: {Describe(e)}");
                }
                _readAt = -1e9;
            }
            ReadThresholds(force: true);
            if (_stop >= 0 && _stop != end)
                _log($"charge control: wrote stop={end} start={start}, firmware reports stop={_stop} start={_start}");
        }

        public void WriteBehaviour(string behaviour)
        {
            if (behaviour != "auto") throw new ChargeException(95, $"charge_behaviour {behaviour} unsupported");
        }

        public int? LoadLimit()
        {
            var saved = LimitStore.Load();
            if (saved != null) return saved;
            // First run: adopt what the EC already has (e.g. set from Linux or Vantage).
            ReadThresholds(force: false);
            return (_enabled > 0 && _stop > 0) ? _stop : (int?)null;
        }

        public void SaveLimit(int limit) => LimitStore.Save(limit);

        // ---- IChargeHardwareInfo ---------------------------------------------------------

        public Dictionary<string, object?> ExtraState()
        {
            ReadThresholds(force: false);
            return new Dictionary<string, object?>
            {
                ["charge_behaviour"] = "auto",
                ["charge_start_threshold"] = _start,
                ["charge_end_threshold"] = _stop,
                ["charge_threshold_enabled"] = _enabled > 0,
                ["control_method"] = "lenovo-power-manager",
            };
        }

        public void Reassert()
        {
            _readAt = -1e9;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_ctx != IntPtr.Zero)
                {
                    var pCtx = Marshal.AllocHGlobal(IntPtr.Size);
                    try
                    {
                        Marshal.WriteIntPtr(pCtx, _ctx);
                        NdrClientCall2(_stubDesc, _procFmt + LenovoRpcFormat.ProcFreeContext, __arglist(pCtx));
                    }
                    catch (Exception) { }
                    finally { Marshal.FreeHGlobal(pCtx); }
                    _ctx = IntPtr.Zero;
                }
                if (_binding != IntPtr.Zero) { RpcBindingFree(ref _binding); _binding = IntPtr.Zero; }
            }
            foreach (var k in _keep) k.Dispose();
            _keep.Clear();
        }
    }

    internal static class LenovoRpcFormat
    {
        // Offsets into lenpwr_proc.bin (MIDL procedure format string, x64):
        // LpcCreateContext = proc 0, LpcFreeContext = proc 1, LpcGetChargeThreshold = proc 35,
        // LpcSetChargeThreshold = proc 40.
        public const int ProcCreateContext = 0, ProcFreeContext = 36, ProcGetChargeThreshold = 1532, ProcSetChargeThreshold = 1806;
    }
}
