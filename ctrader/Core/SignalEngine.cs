using System;
using IntradayMomentum.Models;

namespace IntradayMomentum.Core
{
    public sealed class SignalEngine
    {
        private DateTime _lastProcessed = DateTime.MinValue;
        public Signal Evaluate(Bar bar, NoiseArea area, int sessionStartMinute, int intervalMinutes, int firstSignalMinute, int lastSignalMinute, double minimumBreakoutDistance, KamaMode kamaMode, double kama, double kamaSlope, double slopeThreshold)
        {
            int minute = (int)(bar.Time - bar.Time.Date).TotalMinutes - sessionStartMinute;
            if (!area.Valid || minute <= 0 || minute < firstSignalMinute || minute > lastSignalMinute || intervalMinutes <= 0 || minute % intervalMinutes != 0 || bar.Time <= _lastProcessed) return new Signal(Direction.None, bar.Time, bar.Close, "NO_CHECKPOINT");
            _lastProcessed = bar.Time;
            if (bar.Close > area.Upper + minimumBreakoutDistance && (kamaMode != KamaMode.EntryFilter || (bar.Close > kama && kamaSlope >= slopeThreshold))) return new Signal(Direction.Long, bar.Time, bar.Close, "BAND_BREAK_LONG");
            if (bar.Close < area.Lower - minimumBreakoutDistance && (kamaMode != KamaMode.EntryFilter || (bar.Close < kama && kamaSlope <= -slopeThreshold))) return new Signal(Direction.Short, bar.Time, bar.Close, "BAND_BREAK_SHORT");
            return new Signal(Direction.None, bar.Time, bar.Close, "INSIDE_NOISE_AREA");
        }
    }
}
