// Picks the charge-control hardware for this machine.
using System;
using ClearPower.Core;

namespace ClearPower.Win
{
    public static class ChargeBackends
    {
        public static IChargeHardware Detect(Action<string>? log)
        {
            var lenovo = LenovoPowerManager.TryCreate(log ?? (_ => { }));
            if (lenovo != null) return lenovo;
            log?.Invoke("charge control: no supported vendor interface (Lenovo Power Manager RPC not available)");
            return new NullChargeHardware();
        }
    }
}
