using System;
using System.Linq;
using cAlgo.API;
using IntradayMomentum.Core;
using IntradayMomentum.Models;

namespace IntradayMomentum.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class IntradayMomentumBot : Robot
    {
        [Parameter("Strategy Enabled", Group = "01 - CORE STRATEGY", DefaultValue = false)] public bool StrategyEnabled { get; set; }
        [Parameter("Allow Long", Group = "01 - CORE STRATEGY", DefaultValue = true)] public bool AllowLong { get; set; }
        [Parameter("Allow Short", Group = "01 - CORE STRATEGY", DefaultValue = true)] public bool AllowShort { get; set; }
        [Parameter("Allow Same-Side Re-entry", Group = "01 - CORE STRATEGY", DefaultValue = true)] public bool AllowSameSideReentry { get; set; }
        [Parameter("Allow Opposite Flip", Group = "01 - CORE STRATEGY", DefaultValue = true)] public bool AllowOppositeFlip { get; set; }
        [Parameter("Session Start Hour (NY)", Group = "02 - SESSION & TIME", DefaultValue = 9)] public int SessionStartHour { get; set; }
        [Parameter("Session Start Minute (NY)", Group = "02 - SESSION & TIME", DefaultValue = 30)] public int SessionStartMinute { get; set; }
        [Parameter("Session End Hour (NY)", Group = "02 - SESSION & TIME", DefaultValue = 16)] public int SessionEndHour { get; set; }
        [Parameter("Session End Minute (NY)", Group = "02 - SESSION & TIME", DefaultValue = 0)] public int SessionEndMinute { get; set; }
        [Parameter("Noise Lookback Sessions", Group = "03 - NOISE AREA", DefaultValue = 14)] public int NoiseLookback { get; set; }
        [Parameter("Noise Multiplier", Group = "03 - NOISE AREA", DefaultValue = 1.0)] public double NoiseMultiplier { get; set; }
        [Parameter("Signal Interval Minutes", Group = "04 - ENTRY ENGINE", DefaultValue = 30)] public int SignalInterval { get; set; }
        [Parameter("Minimum Breakout Distance", Group = "04 - ENTRY ENGINE", DefaultValue = 0.0)] public double MinimumBreakoutDistance { get; set; }
        [Parameter("Use VWAP", Group = "05 - VWAP", DefaultValue = true)] public bool UseVwap { get; set; }
        [Parameter("KAMA Mode", Group = "06 - KAMA", DefaultValue = KamaMode.Off)] public KamaMode KamaMode { get; set; }
        [Parameter("KAMA Period", Group = "06 - KAMA", DefaultValue = 10)] public int KamaPeriod { get; set; }
        [Parameter("KAMA Fast", Group = "06 - KAMA", DefaultValue = 2)] public int KamaFast { get; set; }
        [Parameter("KAMA Slow", Group = "06 - KAMA", DefaultValue = 30)] public int KamaSlow { get; set; }
        [Parameter("Risk Per Trade %", Group = "07 - POSITION SIZING", DefaultValue = 0.25)] public double RiskPerTrade { get; set; }
        [Parameter("Initial Stop", Group = "08 - INITIAL STOP", DefaultValue = StopMode.OriginalNoiseVwap)] public StopMode InitialStop { get; set; }
        [Parameter("Enable Break-Even", Group = "09 - BREAKEVEN & PARTIAL", DefaultValue = true)] public bool EnableBreakEven { get; set; }
        [Parameter("Break-Even Trigger R", Group = "09 - BREAKEVEN & PARTIAL", DefaultValue = 1.5)] public double BreakEvenTriggerR { get; set; }
        [Parameter("Partial Close %", Group = "09 - BREAKEVEN & PARTIAL", DefaultValue = 50.0)] public double PartialClosePercent { get; set; }
        [Parameter("Enable ATR Trail", Group = "10 - ATR TRAILING", DefaultValue = true)] public bool EnableAtrTrail { get; set; }
        [Parameter("ATR Period", Group = "10 - ATR TRAILING", DefaultValue = 14)] public int AtrPeriod { get; set; }
        [Parameter("ATR Multiplier", Group = "10 - ATR TRAILING", DefaultValue = 2.0)] public double AtrMultiplier { get; set; }
        [Parameter("Force Flat At Session End", Group = "11 - EXIT ENGINE", DefaultValue = true)] public bool ForceFlat { get; set; }
        [Parameter("Daily Loss Limit %", Group = "12 - PROP / ACCOUNT RISK", DefaultValue = 2.0)] public double DailyLossLimit { get; set; }
        [Parameter("Maximum Spread (pips)", Group = "13 - EXECUTION", DefaultValue = 5.0)] public double MaximumSpread { get; set; }
        [Parameter("Trade Label", Group = "13 - EXECUTION", DefaultValue = "NoiseAreaEA")] public string TradeLabel { get; set; }
        [Parameter("Debug", Group = "15 - LOGGING / DEBUG", DefaultValue = false)] public bool Debug { get; set; }

        private readonly NoiseAreaEngine _noise = new NoiseAreaEngine();
        private readonly SignalEngine _signals = new SignalEngine();
        private readonly VwapEngine _vwap = new VwapEngine();
        private readonly KamaEngine _kama = new KamaEngine();
        private readonly AtrEngine _atr = new AtrEngine();
        private readonly TradeManager _manager = new TradeManager();
        private ExecutionManager _execution;
        private PositionLifecycle _lifecycle;
        private DateTime _lastSignal = DateTime.MinValue;
        private DateTime _riskDay = DateTime.MinValue;
        private double _dailyEquityBaseline;
        private bool _riskLocked;

        protected override void OnStart()
        {
            _execution = new ExecutionManager(this);
            ParameterValidator.Validate(NoiseLookback, NoiseMultiplier, SignalInterval, KamaFast, KamaSlow, PartialClosePercent, AtrPeriod, AtrMultiplier, RiskPerTrade, 4);
            _riskDay = ToNewYork(Server.Time).Date;
            _dailyEquityBaseline = Account.Equity;
            Print("WARNING=AUTOMATED_STRATEGY VERIFY_SYMBOL_TIMEZONE_RISK_VOLUME_SPREAD");
        }

        protected override void OnBar()
        {
            var bar = Bars.Last(1);
            DateTime local = ToNewYork(bar.OpenTime);
            if (local.Date != _riskDay) { _riskDay = local.Date; _dailyEquityBaseline = Account.Equity; _riskLocked = false; }
            if (DailyLossLimit > 0 && Account.Equity <= _dailyEquityBaseline * (1.0 - DailyLossLimit / 100.0)) _riskLocked = true;
            int minute = local.Hour * 60 + local.Minute;
            int start = SessionStartHour * 60 + SessionStartMinute, end = SessionEndHour * 60 + SessionEndMinute;
            if (minute < start || minute >= end) { if (ForceFlat) CloseOwn(ExitReason.EndOfDay); return; }
            _vwap.Update(new Bar(bar.OpenTime, bar.Open, bar.High, bar.Low, bar.Close, bar.TickVolume));
            _atr.Update(new Bar(bar.OpenTime, bar.Open, bar.High, bar.Low, bar.Close, bar.TickVolume), AtrPeriod);
            double kama = _kama.Update(bar.Close, KamaPeriod, KamaFast, KamaSlow, 1);
            if (!StrategyEnabled || _riskLocked || Symbol.Spread > MaximumSpread) return;
            // The full historical session cache is intentionally supplied by the backtest/data adapter.
            // A live instance must not trade until a valid NoiseArea is available.
            var area = BuildArea(bar.OpenTime, local, start);
            if (!area.Valid) return;
            ManagePosition(bar, area);
            var signal = _signals.Evaluate(new Bar(local, bar.Open, bar.High, bar.Low, bar.Close, bar.TickVolume), area, start, SignalInterval, SignalInterval, end - start - SignalInterval, MinimumBreakoutDistance, KamaMode, kama, 0, 0);
            if (signal.IsValid && signal.Time != _lastSignal) { _lastSignal = signal.Time; ProcessSignal(signal, area); }
        }

        private DateTime ToNewYork(DateTime utc)
        {
            try { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "America/New_York"); }
            catch { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Eastern Standard Time"); }
        }

        private NoiseArea BuildArea(DateTime currentUtc, DateTime currentLocal, int start)
        {
            var sessions = new System.Collections.Generic.Dictionary<DateTime, System.Collections.Generic.List<double>>();
            var sessionOpens = new System.Collections.Generic.Dictionary<DateTime, double>();
            int end = SessionEndHour * 60 + SessionEndMinute;
            for (int i = 0; i < Bars.Count; i++)
            {
                DateTime local = ToNewYork(Bars.OpenTimes[i]);
                int minute = local.Hour * 60 + local.Minute;
                if (local.Date >= currentLocal.Date || minute < start || minute >= end) continue;
                if (!sessions.ContainsKey(local.Date)) sessions[local.Date] = new System.Collections.Generic.List<double>();
                if (!sessionOpens.ContainsKey(local.Date)) sessionOpens[local.Date] = Bars.OpenPrices[i];
                sessions[local.Date].Add(Bars.ClosePrices[i]);
            }
            var completed = sessions.OrderByDescending(x => x.Key).Take(NoiseLookback).ToList();
            if (completed.Count < NoiseLookback) return new NoiseArea(false, 0, 0, 0);
            var today = new System.Collections.Generic.List<double>();
            double sessionOpen = 0;
            for (int i = 0; i < Bars.Count; i++)
            {
                DateTime local = ToNewYork(Bars.OpenTimes[i]);
                if (local.Date == currentLocal.Date && local <= currentLocal && local.Hour * 60 + local.Minute >= start && local.Hour * 60 + local.Minute < end)
                { if (today.Count == 0) sessionOpen = Bars.OpenPrices[i]; today.Add(Bars.ClosePrices[i]); }
            }
            if (today.Count == 0) return new NoiseArea(false, 0, 0, 0);
            double previousClose = completed[0].Value[completed[0].Value.Count - 1];
            int currentMinute = currentLocal.Hour * 60 + currentLocal.Minute - start;
            var observations = new System.Collections.Generic.List<double[]>();
            foreach (var session in completed)
            {
                if (session.Value.Count > currentMinute) observations.Add(new[] { sessionOpens[session.Key], session.Value[currentMinute] });
            }
            return _noise.Calculate(sessionOpen, previousClose, 0, NoiseMultiplier, observations);
        }
        private void ProcessSignal(Signal signal, NoiseArea area)
        {
            var own = Positions.FindAll(TradeLabel, SymbolName);
            if (own.Length > 0)
            {
                if (own[0].TradeType == (signal.Direction == Direction.Long ? TradeType.Buy : TradeType.Sell)) { if (!AllowSameSideReentry) return; return; }
                if (!AllowOppositeFlip) return;
                _execution.Close(own[0]);
            }
            if ((signal.Direction == Direction.Long && !AllowLong) || (signal.Direction == Direction.Short && !AllowShort)) return;
            double stop = signal.Direction == Direction.Long ? area.Lower : area.Upper;
            if (UseVwap && _vwap.Value > 0) stop = signal.Direction == Direction.Long ? Math.Min(stop, _vwap.Value) : Math.Max(stop, _vwap.Value);
            double volume = PositionSizer.FixedRisk(Account.Equity, RiskPerTrade, signal.Price, stop, Symbol.TickSize, Symbol.TickValue, Symbol.VolumeInUnitsMin, Symbol.VolumeInUnitsMax, Symbol.VolumeInUnitsStep);
            if (volume <= 0 || (signal.Direction == Direction.Long && stop >= Symbol.Bid) || (signal.Direction == Direction.Short && stop <= Symbol.Ask)) { Print("EVENT=ENTRY_REJECTED REASON=INVALID_SIZE_OR_STOP"); return; }
            var result = _execution.Open(TradeLabel, signal.Direction == Direction.Long ? TradeType.Buy : TradeType.Sell, volume, stop, "signal=" + signal.Time.ToString("O"));
            if (result != null && result.IsSuccessful && result.Position != null) { var p = result.Position; _lifecycle = new PositionLifecycle { PositionId = p.Id.ToString(), Direction = signal.Direction, SignalTime = signal.Time, EntryTime = Server.Time, RequestedPrice = signal.Price, ActualFillPrice = p.EntryPrice, InitialStop = p.StopLoss ?? stop, CurrentStop = p.StopLoss ?? stop, RiskDistance = Math.Abs(p.EntryPrice - (p.StopLoss ?? stop)), HighestFavorablePrice = p.EntryPrice, LowestFavorablePrice = p.EntryPrice }; Print("EVENT=ENTRY DIRECTION={0} ENTRY={1} STOP={2}", signal.Direction, p.EntryPrice, p.StopLoss); }
        }

        private void ManagePosition(Bar bar, NoiseArea area)
        {
            var p = Positions.Find(TradeLabel, SymbolName); if (p == null || _lifecycle == null) return;
            var decision = _manager.Evaluate(_lifecycle, bar.Close, bar.High, bar.Low, _atr.Value, EnableBreakEven ? BreakEvenTriggerR : double.MaxValue, PartialClosePercent, EnableAtrTrail ? AtrMultiplier : 0, 0, Symbol.VolumeInUnitsMin, Symbol.VolumeInUnitsStep, p.VolumeInUnits);
            if (decision.Partial && !_execution.Partial(p, decision.PartialVolume)) _lifecycle.PartialTaken = false;
            if (decision.ChangeStop && decision.Stop > 0) _execution.Modify(p, decision.Stop);
        }
        private void CloseOwn(ExitReason reason) { foreach (var p in Positions.FindAll(TradeLabel, SymbolName)) { Print("EVENT=EXIT REASON={0}", reason); _execution.Close(p); } }
    }
}
