using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace IntradayMomentum
{
    public enum Direction { None, Long, Short }
    public enum KamaMode { Off, EntryFilter, ExitFilter, ReplaceInterval }
    public enum StopMode { OriginalNoiseVwap, OppositeBand, Atr, FixedDistance }
    public enum ExitReason { None, EndOfDay, OppositeSignal, DailyRiskLimit, ExecutionFailure }

    public struct ResearchBar
    {
        public ResearchBar(DateTime time, double open, double high, double low, double close, double volume)
        { Time = time; OpenTime = time; Open = open; High = high; Low = low; Close = close; Volume = volume; }
        public DateTime Time, OpenTime; public double Open, High, Low, Close, Volume;
    }

    public struct NoiseArea
    {
        public NoiseArea(bool valid, double sigma, double upper, double lower)
        { Valid = valid; Sigma = sigma; Upper = upper; Lower = lower; }
        public bool Valid; public double Sigma, Upper, Lower;
    }

    public struct Signal
    {
        public Signal(Direction direction, DateTime time, double price, string reason)
        { Direction = direction; Time = time; Price = price; Reason = reason; }
        public Direction Direction; public DateTime Time; public double Price; public string Reason;
        public bool IsValid { get { return Direction != Direction.None; } }
    }

    public sealed class PositionLifecycle
    {
        public string PositionId = ""; public Direction Direction; public DateTime SignalTime, EntryTime;
        public double RequestedPrice, ActualFillPrice, InitialStop, RiskDistance, CurrentStop;
        public double HighestFavorablePrice, LowestFavorablePrice;
        public bool BreakEvenActivated, PartialTaken, AtrTrailActivated;
        public double CurrentR(double price) { return RiskDistance <= 0 ? 0 : (Direction == Direction.Long ? price - ActualFillPrice : ActualFillPrice - price) / RiskDistance; }
    }

    public sealed class NoiseAreaEngine
    {
        public NoiseArea Calculate(double sessionOpen, double previousClose, double multiplier, List<double[]> observations)
        {
            if (sessionOpen <= 0 || observations == null || observations.Count == 0) return new NoiseArea(false, 0, 0, 0);
            double sigma = observations.Average(x => Math.Abs(x[1] / x[0] - 1.0));
            double upperAnchor = Math.Max(sessionOpen, previousClose), lowerAnchor = Math.Min(sessionOpen, previousClose);
            return new NoiseArea(true, sigma, upperAnchor * (1 + multiplier * sigma), lowerAnchor * (1 - multiplier * sigma));
        }
    }

    public sealed class VwapEngine
    {
        private double _priceVolume, _volume;
        public double Value { get { return _volume <= 0 ? 0 : _priceVolume / _volume; } }
        public double Update(double high, double low, double close, double volume)
        { double v = Math.Max(0, volume); _priceVolume += ((high + low + close) / 3.0) * v; _volume += v; return Value; }
    }

    public sealed class KamaEngine
    {
        private readonly List<double> _prices = new List<double>();
        public double Value, Slope;
        public double Update(double price, int period, int fast, int slow)
        {
            _prices.Add(price); if (_prices.Count == 1) { Value = price; return Value; }
            if (_prices.Count <= period) { Value = price; return Value; }
            double change = Math.Abs(price - _prices[_prices.Count - 1 - period]), volatility = 0;
            for (int i = _prices.Count - period; i < _prices.Count; i++) volatility += Math.Abs(_prices[i] - _prices[i - 1]);
            double er = volatility <= 1e-12 ? 0 : change / volatility;
            double fastSc = 2.0 / (fast + 1), slowSc = 2.0 / (slow + 1), sc = Math.Pow(er * (fastSc - slowSc) + slowSc, 2);
            double previous = Value; Value = previous + sc * (price - previous); Slope = Value - previous; return Value;
        }
    }

    public sealed class AtrEngine
    {
        private readonly Queue<double> _trs = new Queue<double>(); private double _previousClose; public double Value;
        public double Update(double high, double low, double close, int period)
        { double tr = _trs.Count == 0 ? high - low : Math.Max(high - low, Math.Max(Math.Abs(high - _previousClose), Math.Abs(low - _previousClose))); _previousClose = close; _trs.Enqueue(tr); while (_trs.Count > Math.Max(1, period)) _trs.Dequeue(); Value = _trs.Average(); return Value; }
    }

    public sealed class TradeManager
    {
        public bool Manage(PositionLifecycle p, double price, double high, double low, double atr, double triggerR, double partialPercent, double atrMultiplier, double volume, double volumeStep, double minimumVolume, out double newStop, out double partialVolume)
        {
            newStop = p.CurrentStop; partialVolume = 0; if (p == null || p.RiskDistance <= 0) return false;
            if (p.Direction == Direction.Long) p.HighestFavorablePrice = Math.Max(p.HighestFavorablePrice, high); else p.LowestFavorablePrice = p.LowestFavorablePrice == 0 ? low : Math.Min(p.LowestFavorablePrice, low);
            bool changed = false; bool trigger = p.CurrentR(price) >= triggerR - 1e-10;
            if (trigger && !p.BreakEvenActivated) { p.BreakEvenActivated = true; newStop = p.ActualFillPrice; changed = true; }
            if (trigger && !p.PartialTaken && partialPercent > 0 && partialPercent < 100)
            { partialVolume = Math.Floor((volume * partialPercent / 100.0) / volumeStep) * volumeStep; if (partialVolume >= minimumVolume && volume - partialVolume >= minimumVolume) p.PartialTaken = true; }
            if (trigger && atr > 0) { p.AtrTrailActivated = true; double candidate = p.Direction == Direction.Long ? p.HighestFavorablePrice - atr * atrMultiplier : p.LowestFavorablePrice + atr * atrMultiplier; newStop = p.Direction == Direction.Long ? Math.Max(newStop, candidate) : Math.Min(newStop, candidate); changed = true; }
            newStop = p.Direction == Direction.Long ? Math.Max(p.CurrentStop, newStop) : Math.Min(p.CurrentStop, newStop); p.CurrentStop = newStop; return changed;
        }
    }

    public sealed class ExecutionManager
    {
        private readonly Robot _robot;
        public ExecutionManager(Robot robot) { _robot = robot; }
        public TradeResult Open(string label, TradeType type, double volume, double stop, string comment)
        {
            try { double reference = type == TradeType.Buy ? _robot.Symbol.Ask : _robot.Symbol.Bid; double stopPips = Math.Abs(reference - stop) / _robot.Symbol.PipSize; return _robot.ExecuteMarketOrder(type, _robot.SymbolName, volume, label, stopPips, null, comment, false); }
            catch (Exception ex) { _robot.Print("EVENT=EXECUTION_FAILURE OP=OPEN ERROR={0}", ex.Message); return null; }
        }
        public bool Modify(Position position, double stop)
        {
            if (position == null) return false; bool valid = position.TradeType == TradeType.Buy ? stop < _robot.Symbol.Bid : stop > _robot.Symbol.Ask; if (!valid) return false;
            try { TradeResult result = _robot.ModifyPosition(position, stop, position.TakeProfit, ProtectionType.Absolute); return result != null && result.IsSuccessful; } catch (Exception ex) { _robot.Print("EVENT=EXECUTION_FAILURE OP=MODIFY ERROR={0}", ex.Message); return false; }
        }
        public bool Partial(Position position, double volume) { try { TradeResult result = _robot.ClosePosition(position, volume); return result != null && result.IsSuccessful; } catch { return false; } }
        public bool Close(Position position) { try { TradeResult result = _robot.ClosePosition(position); return result != null && result.IsSuccessful; } catch { return false; } }
    }

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class IntradayMomentumBot : Robot
    {
        [Parameter("Strategy Enabled", Group = "01 - CORE STRATEGY", DefaultValue = false)] public bool StrategyEnabled { get; set; }
        [Parameter("Allow Long", Group = "01 - CORE STRATEGY", DefaultValue = true)] public bool AllowLong { get; set; }
        [Parameter("Allow Short", Group = "01 - CORE STRATEGY", DefaultValue = true)] public bool AllowShort { get; set; }
        [Parameter("Noise Lookback Sessions", Group = "03 - NOISE AREA", DefaultValue = 14)] public int NoiseLookback { get; set; }
        [Parameter("Noise Multiplier", Group = "03 - NOISE AREA", DefaultValue = 1.0)] public double NoiseMultiplier { get; set; }
        [Parameter("Signal Interval Minutes", Group = "04 - ENTRY ENGINE", DefaultValue = 30)] public int SignalInterval { get; set; }
        [Parameter("KAMA Mode", Group = "06 - KAMA", DefaultValue = KamaMode.Off)] public KamaMode KamaMode { get; set; }
        [Parameter("KAMA Period", Group = "06 - KAMA", DefaultValue = 10)] public int KamaPeriod { get; set; }
        [Parameter("KAMA Fast", Group = "06 - KAMA", DefaultValue = 2)] public int KamaFast { get; set; }
        [Parameter("KAMA Slow", Group = "06 - KAMA", DefaultValue = 30)] public int KamaSlow { get; set; }
        [Parameter("Risk Per Trade %", Group = "07 - POSITION SIZING", DefaultValue = 0.25)] public double RiskPerTrade { get; set; }
        [Parameter("Break-Even Trigger R", Group = "09 - BREAKEVEN & PARTIAL", DefaultValue = 1.5)] public double BreakEvenTriggerR { get; set; }
        [Parameter("Partial Close %", Group = "09 - BREAKEVEN & PARTIAL", DefaultValue = 50.0)] public double PartialClosePercent { get; set; }
        [Parameter("Enable ATR Trail", Group = "10 - ATR TRAILING", DefaultValue = true)] public bool EnableAtrTrail { get; set; }
        [Parameter("ATR Period", Group = "10 - ATR TRAILING", DefaultValue = 14)] public int AtrPeriod { get; set; }
        [Parameter("ATR Multiplier", Group = "10 - ATR TRAILING", DefaultValue = 2.0)] public double AtrMultiplier { get; set; }
        [Parameter("Force Flat At Session End", Group = "11 - EXIT ENGINE", DefaultValue = true)] public bool ForceFlat { get; set; }
        [Parameter("Daily Loss Limit %", Group = "12 - PROP / ACCOUNT RISK", DefaultValue = 2.0)] public double DailyLossLimit { get; set; }
        [Parameter("Maximum Spread (pips)", Group = "13 - EXECUTION", DefaultValue = 5.0)] public double MaximumSpread { get; set; }
        [Parameter("Trade Label", Group = "13 - EXECUTION", DefaultValue = "NoiseAreaEA")] public string TradeLabel { get; set; }

        private readonly NoiseAreaEngine _noise = new NoiseAreaEngine(); private readonly VwapEngine _vwap = new VwapEngine(); private readonly KamaEngine _kama = new KamaEngine(); private readonly AtrEngine _atr = new AtrEngine(); private readonly TradeManager _manager = new TradeManager();
        private ExecutionManager _execution; private PositionLifecycle _lifecycle; private DateTime _riskDay, _lastSignal = DateTime.MinValue; private double _dailyBaseline; private bool _riskLocked;

        protected override void OnStart()
        {
            if (NoiseLookback < 2 || NoiseMultiplier <= 0 || SignalInterval <= 0 || KamaFast < 1 || KamaSlow <= KamaFast || PartialClosePercent <= 0 || PartialClosePercent >= 100 || AtrPeriod < 1 || AtrMultiplier <= 0 || RiskPerTrade <= 0) throw new ArgumentException("Invalid strategy parameters");
            _execution = new ExecutionManager(this); _riskDay = ToNewYork(Server.Time).Date; _dailyBaseline = Account.Equity; Print("WARNING=AUTOMATED_STRATEGY VERIFY_SYMBOL_TIMEZONE_RISK_VOLUME_SPREAD");
        }

        protected override void OnBar()
        {
            var b = Bars.Last(1); DateTime local = ToNewYork(b.OpenTime); int minute = local.Hour * 60 + local.Minute, start = 9 * 60 + 30, end = 16 * 60;
            if (local.Date != _riskDay) { _riskDay = local.Date; _dailyBaseline = Account.Equity; _riskLocked = false; }
            if (DailyLossLimit > 0 && Account.Equity <= _dailyBaseline * (1 - DailyLossLimit / 100.0)) _riskLocked = true;
            if (minute < start || minute >= end) { if (ForceFlat) CloseOwn(ExitReason.EndOfDay); return; }
            _vwap.Update(b.High, b.Low, b.Close, b.TickVolume); _atr.Update(b.High, b.Low, b.Close, AtrPeriod); double kama = _kama.Update(b.Close, KamaPeriod, KamaFast, KamaSlow);
            if (!StrategyEnabled || _riskLocked || Symbol.Spread > MaximumSpread) return;
            NoiseArea area = BuildArea(local, start, end); if (!area.Valid) return;
            int sessionMinute = minute - start;
            if (sessionMinute <= 0 || sessionMinute % SignalInterval != 0) return;
            Direction direction = b.Close > area.Upper && (KamaMode != KamaMode.EntryFilter || (b.Close > kama && _kama.Slope >= 0)) ? Direction.Long : b.Close < area.Lower && (KamaMode != KamaMode.EntryFilter || (b.Close < kama && _kama.Slope <= 0)) ? Direction.Short : Direction.None;
            if (direction == Direction.None || local == _lastSignal) return; _lastSignal = local; ProcessSignal(direction, b.Close, local, area);
            ManagePosition(b.Close, b.High, b.Low);
        }

        private DateTime ToNewYork(DateTime utc) { try { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "America/New_York"); } catch { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Eastern Standard Time"); } }
        private NoiseArea BuildArea(DateTime current, int start, int end)
        {
            var sessions = new Dictionary<DateTime, List<double>>(); var opens = new Dictionary<DateTime, double>();
            for (int i = 0; i < Bars.Count; i++) { DateTime t = ToNewYork(Bars.OpenTimes[i]); int m = t.Hour * 60 + t.Minute; if (t.Date >= current.Date || m < start || m >= end) continue; if (!sessions.ContainsKey(t.Date)) { sessions[t.Date] = new List<double>(); opens[t.Date] = Bars.OpenPrices[i]; } sessions[t.Date].Add(Bars.ClosePrices[i]); }
            var completed = sessions.OrderByDescending(x => x.Key).Take(NoiseLookback).ToList(); if (completed.Count < NoiseLookback) return new NoiseArea(false, 0, 0, 0);
            double sessionOpen = 0; int currentMinute = current.Hour * 60 + current.Minute - start;
            for (int i = 0; i < Bars.Count; i++) { DateTime t = ToNewYork(Bars.OpenTimes[i]); int m = t.Hour * 60 + t.Minute; if (t.Date == current.Date && t <= current && m >= start && m < end) { sessionOpen = sessionOpen == 0 ? Bars.OpenPrices[i] : sessionOpen; } }
            var observations = completed.Where(x => x.Value.Count > currentMinute).Select(x => new[] { opens[x.Key], x.Value[currentMinute] }).ToList(); if (sessionOpen <= 0 || observations.Count < NoiseLookback) return new NoiseArea(false, 0, 0, 0);
            return _noise.Calculate(sessionOpen, completed[0].Value.Last(), NoiseMultiplier, observations);
        }
        private void ProcessSignal(Direction direction, double price, DateTime time, NoiseArea area)
        {
            var own = Positions.FindAll(TradeLabel, SymbolName); TradeType type = direction == Direction.Long ? TradeType.Buy : TradeType.Sell;
            if (own.Length > 0) { if (own[0].TradeType == type || own.Length > 0) return; }
            double stop = direction == Direction.Long ? Math.Min(area.Lower, _vwap.Value) : Math.Max(area.Upper, _vwap.Value); if ((direction == Direction.Long && !AllowLong) || (direction == Direction.Short && !AllowShort)) return;
            double riskCash = Account.Equity * RiskPerTrade / 100.0, cashPerUnit = Math.Abs(price - stop) / Symbol.TickSize * Symbol.TickValue, volume = cashPerUnit <= 0 ? 0 : Math.Floor((riskCash / cashPerUnit) / Symbol.VolumeInUnitsStep) * Symbol.VolumeInUnitsStep;
            if (volume < Symbol.VolumeInUnitsMin || (direction == Direction.Long && stop >= Symbol.Bid) || (direction == Direction.Short && stop <= Symbol.Ask)) { Print("EVENT=ENTRY_REJECTED REASON=INVALID_SIZE_OR_STOP"); return; }
            TradeResult result = _execution.Open(TradeLabel, type, volume, stop, "signal=" + time.ToString("O")); if (result == null || !result.IsSuccessful || result.Position == null) return;
            Position p = result.Position; double actualStop = p.StopLoss.HasValue ? p.StopLoss.Value : stop; _lifecycle = new PositionLifecycle { PositionId = p.Id.ToString(), Direction = direction, SignalTime = time, EntryTime = Server.Time, RequestedPrice = price, ActualFillPrice = p.EntryPrice, InitialStop = actualStop, CurrentStop = actualStop, RiskDistance = Math.Abs(p.EntryPrice - actualStop), HighestFavorablePrice = p.EntryPrice, LowestFavorablePrice = p.EntryPrice }; Print("EVENT=ENTRY DIRECTION={0} ENTRY={1} STOP={2}", direction, p.EntryPrice, actualStop);
        }
        private void ManagePosition(double close, double high, double low)
        {
            Position p = Positions.Find(TradeLabel, SymbolName); if (p == null || _lifecycle == null) return; double stop, partial; bool changed = _manager.Manage(_lifecycle, close, high, low, _atr.Value, BreakEvenTriggerR, PartialClosePercent, EnableAtrTrail ? AtrMultiplier : 0, p.VolumeInUnits, Symbol.VolumeInUnitsStep, Symbol.VolumeInUnitsMin, out stop, out partial);
            if (partial > 0 && !_execution.Partial(p, partial)) _lifecycle.PartialTaken = false; if (changed) _execution.Modify(p, stop);
        }
        private void CloseOwn(ExitReason reason) { foreach (Position p in Positions.FindAll(TradeLabel, SymbolName)) { Print("EVENT=EXIT REASON={0}", reason); _execution.Close(p); } }
    }
}
