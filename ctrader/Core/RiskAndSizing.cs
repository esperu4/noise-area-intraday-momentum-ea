using System;
using IntradayMomentum.Models;

namespace IntradayMomentum.Core
{
    public sealed class RiskManager
    {
        public bool DailyLocked { get; private set; }
        public bool DrawdownLocked { get; private set; }
        public bool IsLocked => DailyLocked || DrawdownLocked;
        public void Update(double dailyPnlPercent, double drawdownPercent, double dailyLimit, double maxDrawdown, bool dailyEnabled, bool drawdownEnabled)
        { DailyLocked = dailyEnabled && dailyPnlPercent <= -Math.Abs(dailyLimit); DrawdownLocked = drawdownEnabled && drawdownPercent >= Math.Abs(maxDrawdown); }
    }

    public static class PositionSizer
    {
        public static double FixedRisk(double equity, double riskPercent, double entry, double stop, double tickSize, double tickValue, double min, double max, double step)
        {
            if (equity <= 0 || riskPercent <= 0 || tickSize <= 0 || tickValue <= 0 || Math.Abs(entry - stop) <= 0 || step <= 0) return 0;
            double riskCash = equity * riskPercent / 100.0, cashPerUnit = Math.Abs(entry - stop) / tickSize * tickValue;
            double raw = riskCash / cashPerUnit; double sized = Math.Floor(raw / step) * step;
            return sized < min ? 0 : Math.Min(max, sized);
        }
    }

    public static class ParameterValidator
    {
        public static void Validate(int lookback, double multiplier, int interval, int kamaFast, int kamaSlow, double partial, int atrPeriod, double atrMultiplier, double risk, double leverage)
        {
            if (lookback < 2) throw new ArgumentException("NoiseAreaLookbackDays must be >= 2");
            if (multiplier <= 0 || interval <= 0 || kamaFast < 1 || kamaSlow <= kamaFast || partial <= 0 || partial >= 100 || atrPeriod < 1 || atrMultiplier <= 0 || risk <= 0 || leverage <= 0) throw new ArgumentException("Invalid strategy parameters");
        }
    }
}
