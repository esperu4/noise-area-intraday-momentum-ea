using System;
using IntradayMomentum.Models;

namespace IntradayMomentum.Core
{
    public sealed class TradeManager
    {
        public TradeDecision Evaluate(PositionLifecycle p, double price, double high, double low, double atr, double triggerR, double partialPercent, double atrMultiplier, double breakEvenBuffer, double minimumVolume, double volumeStep, double currentVolume)
        {
            if (p == null || p.RiskDistance <= 0) return new TradeDecision(false, 0, false, 0, false, ExitReason.None);
            if (p.Direction == Direction.Long) { p.HighestFavorablePrice = Math.Max(p.HighestFavorablePrice, high); }
            else { p.LowestFavorablePrice = p.LowestFavorablePrice == 0 ? low : Math.Min(p.LowestFavorablePrice, low); }
            double r = p.CurrentR(price); bool trigger = r + 1e-10 >= triggerR; bool changed = false; double stop = p.CurrentStop; bool partial = false; double partialVolume = 0;
            if (trigger && !p.BreakEvenActivated) { p.BreakEvenActivated = true; changed = true; stop = p.Direction == Direction.Long ? p.ActualFillPrice + breakEvenBuffer : p.ActualFillPrice - breakEvenBuffer; }
            if (trigger && !p.PartialTaken && partialPercent > 0 && partialPercent < 100)
            {
                double raw = currentVolume * partialPercent / 100.0; partialVolume = Math.Floor(raw / volumeStep) * volumeStep;
                if (partialVolume >= minimumVolume && currentVolume - partialVolume >= minimumVolume) { p.PartialTaken = true; partial = true; }
            }
            if (trigger && atr > 0) { p.AtrTrailActivated = true; double candidate = p.Direction == Direction.Long ? p.HighestFavorablePrice - atr * atrMultiplier : p.LowestFavorablePrice + atr * atrMultiplier; stop = p.Direction == Direction.Long ? Math.Max(stop, candidate) : Math.Min(stop, candidate); changed = true; }
            if (p.Direction == Direction.Long) stop = Math.Max(stop, p.CurrentStop); else stop = Math.Min(stop, p.CurrentStop == 0 ? double.MaxValue : p.CurrentStop);
            p.CurrentStop = stop;
            return new TradeDecision(changed, stop, partial, partialVolume, false, ExitReason.None);
        }
    }
}
