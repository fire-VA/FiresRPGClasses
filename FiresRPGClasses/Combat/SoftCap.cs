using System;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// A thin forwarder to Core's FiresCore.Curves.SoftCap, the ONE authority for diminishing returns. Kept so existing callers don't change. Core's curve works on positive values; RPGClasses passes signed ones
    /// (negative = a reduction: stamina cost, damage taken), so the curve runs on the magnitude and the sign is put back.
    /// Soft(raw, knee): raw up to the knee, then knee + knee * ln(1 + (raw - knee) / knee), rising forever.
    /// Asymptote(raw, knee, limit): raw up to the knee, then an exponential approach to the limit, never reaching it.
    /// </summary>
    public static class SoftCap
    {
        public static float Soft(float raw, float knee)
        {
            if (knee <= 0f || float.IsNaN(raw)) return raw;
            float soft = FiresCore.Curves.SoftCap.Soft(Math.Abs(raw), knee);
            return raw < 0f ? -soft : soft;
        }

        public static float Asymptote(float raw, float knee, float limit)
        {
            if (knee <= 0f || float.IsNaN(raw)) return raw;
            float soft = FiresCore.Curves.SoftCap.Asymptote(Math.Abs(raw), knee, limit);
            return raw < 0f ? -soft : soft;
        }
    }
}
