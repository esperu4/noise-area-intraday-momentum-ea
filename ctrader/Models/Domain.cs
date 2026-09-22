using System;

namespace IntradayMomentum.Models
{
    public enum Direction { None, Long, Short }
    public enum BotState { Flat, SignalPending, Long, Short, LongBe, ShortBe, LongTrailing, ShortTrailing, RiskLock, SessionClosed }
    public enum KamaMode { Off, EntryFilter, ExitFilter, ReplaceInterval }
    public enum ExitReason { None, NoiseStop, VwapStop, AtrStop, BreakEven, FixedTp, EndOfDay, OppositeSignal, DailyRiskLimit, MaximumDrawdown, Manual, ExecutionFailure }
    public enum StopMode { OriginalNoiseVwap, OppositeBand, Atr, FixedDistance }
    public enum SizingMode { FixedRisk, VolatilityTarget, FixedUnits }

    public readonly struct Bar
    {
        public Bar(DateTime time, double open, double high, double low, double close, double volume)
        { Time = time; Open = open; High = high; Low = low; Close = close; Volume = volume; }
        public DateTime Time { get; }
        public double Open { get; }
        public double High { get; }
        public double Low { get; }
        public double Close { get; }
        public double Volume { get; }
    }

    public readonly struct NoiseArea
    {
        public NoiseArea(bool valid, double sigma, double upper, double lower)
        { Valid = valid; Sigma = sigma; Upper = upper; Lower = lower; }
        public bool Valid { get; }
        public double Sigma { get; }
        public double Upper { get; }
        public double Lower { get; }
    }

    public readonly struct Signal
    {
        public Signal(Direction direction, DateTime time, double price, string reason)
        { Direction = direction; Time = time; Price = price; Reason = reason; }
        public Direction Direction { get; }
        public DateTime Time { get; }
        public double Price { get; }
        public string Reason { get; }
        public bool IsValid => Direction != Direction.None;
    }

    public sealed class PositionLifecycle
    {
        public string PositionId { get; set; } = "";
        public Direction Direction { get; set; }
        public DateTime SignalTime { get; set; }
        public DateTime EntryTime { get; set; }
        public double RequestedPrice { get; set; }
        public double ActualFillPrice { get; set; }
        public double InitialStop { get; set; }
        public double RiskDistance { get; set; }
        public double CurrentStop { get; set; }
        public double HighestFavorablePrice { get; set; }
        public double LowestFavorablePrice { get; set; }
        public bool BreakEvenActivated { get; set; }
        public bool PartialTaken { get; set; }
        public bool AtrTrailActivated { get; set; }
        public double CurrentR(double price) => RiskDistance <= 0 ? 0 : (Direction == Direction.Long ? (price - ActualFillPrice) : (ActualFillPrice - price)) / RiskDistance;
    }

    public readonly struct TradeDecision
    {
        public TradeDecision(bool changeStop, double stop, bool partial, double partialVolume, bool close, ExitReason reason)
        { ChangeStop = changeStop; Stop = stop; Partial = partial; PartialVolume = partialVolume; Close = close; Reason = reason; }
        public bool ChangeStop { get; }
        public double Stop { get; }
        public bool Partial { get; }
        public double PartialVolume { get; }
        public bool Close { get; }
        public ExitReason Reason { get; }
    }
}
