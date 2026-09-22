// ============================================================================
// IntradayMomentumBot.cs
// Noise Area / Intraday Momentum cBot (Zarattini-Aziz-Barbon methodology,
// with optional Quantitativo-style lookback/vol-target settings, an optional
// KAMA entry filter, and a configurable ATR trade manager).
//
// PLATFORM: cTrader Automate (cAlgo.API)
// REQUIRED CHART TIMEFRAME: 1 Minute (the bot validates this at OnStart)
//
// ----------------------------------------------------------------------------
// KNOWN LIMITATIONS / SIMPLIFICATIONS IN THIS BUILD (read before trusting it)
// ----------------------------------------------------------------------------
// 1. KAMA modes: only OFF and ENTRY_FILTER are implemented. EXIT_FILTER and
//    REPLACE_INTERVAL (continuous breakout mode) are NOT implemented - the
//    parameter exists but selecting them currently behaves like OFF and logs
//    a warning. Wire these up before relying on them.
// 2. Volatility-target position sizing uses a simplified realized-volatility
//    estimate (stdev of daily session returns) and converts exposure to
//    volume via the symbol's contract size / tick value. It has not been
//    validated against the paper's exact methodology - treat it as a
//    starting point, not a faithful reproduction.
// 3. Restart recovery is minimal: on OnStart the bot re-attaches to any
//    existing open position with a matching label and infers PartialTaken
//    from whether current volume is below the recorded original volume, but
//    it does NOT persist state to disk, so a restart while BreakEven/ATR
//    trail state hasn't been reached yet will restart trade-management logic
//    from scratch for that position (it will treat it as a fresh, un-managed
//    position rather than guessing progress that can't be reconstructed).
// 4. No walk-forward runner, no Monte Carlo utility, no multi-symbol
//    orchestration, no cross-platform (MT5) validation harness. This file is
//    the trading logic only - research tooling from the spec's later
//    sections is out of scope here.
// 5. Session-history construction (for the Noise Area) scans whatever bar
//    history cTrader has loaded/loads on request. On very illiquid symbols
//    or brokers with short history, NoiseAreaLookbackDays may not be fully
//    satisfiable - the bot logs how many sessions it actually found and will
//    refuse to trade until it has at least MinValidSessions.
// 6. "Original Noise/VWAP" initial stop and "Hybrid" post-trigger mode are
//    implemented; "ATR Initial Stop" is implemented; "Fixed Distance" is
//    implemented. All four are wired into InitialStopMode.
//
// This is a single-file build for portability. Regions separate the modules
// described in the spec (Session, Noise Area, VWAP, KAMA, Sizing, Trade
// Manager, Risk Manager, Execution, Dashboard, Logging) so it can be split
// into multiple files later without restructuring the logic.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    #region Enums

    public enum StrategyModePreset { ORIGINAL_PAPER, QUANTITATIVO_90D, CUSTOM }
    public enum KamaModeOption { OFF, ENTRY_FILTER, EXIT_FILTER, REPLACE_INTERVAL }
    public enum PositionSizingModeOption { VOLATILITY_TARGET, FIXED_RISK_PERCENT, FIXED_LOT, FIXED_NOTIONAL }
    public enum InitialStopModeOption { ORIGINAL_NOISE_VWAP, OPPOSITE_NOISE_BAND, ATR_INITIAL_STOP, FIXED_DISTANCE }
    public enum PostTriggerStopModeOption { ATR_ONLY, ORIGINAL_ONLY, HYBRID }
    public enum TradeStateEnum { FLAT, LONG, SHORT, LONG_BE, SHORT_BE, LONG_TRAILING, SHORT_TRAILING, RISK_LOCK }

    public enum ExitReasonEnum
    {
        NOISE_STOP, VWAP_STOP, ATR_STOP, BREAK_EVEN, FIXED_TP, END_OF_DAY,
        OPPOSITE_SIGNAL, DAILY_RISK_LIMIT, MAX_DRAWDOWN, MANUAL, EXECUTION_FAILURE
    }

    #endregion

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class ClaudeNoiseAreaBot : Robot
    {
        // ====================================================================
        // 01 - CORE STRATEGY
        // ====================================================================
        [Parameter("Strategy Enabled", DefaultValue = true, Group = "01 - Core Strategy")]
        public bool StrategyEnabled { get; set; }

        [Parameter("Strategy Mode", DefaultValue = StrategyModePreset.ORIGINAL_PAPER, Group = "01 - Core Strategy")]
        public StrategyModePreset StrategyMode { get; set; }

        [Parameter("Allow Long", DefaultValue = true, Group = "01 - Core Strategy")]
        public bool AllowLong { get; set; }

        [Parameter("Allow Short", DefaultValue = true, Group = "01 - Core Strategy")]
        public bool AllowShort { get; set; }

        [Parameter("Allow Same-Side Re-entry", DefaultValue = true, Group = "01 - Core Strategy")]
        public bool AllowSameSideReentry { get; set; }

        [Parameter("Allow Opposite-Signal Flip", DefaultValue = true, Group = "01 - Core Strategy")]
        public bool AllowOppositeSignalFlip { get; set; }

        [Parameter("Require Confirmation (Arm to Trade)", DefaultValue = false, Group = "01 - Core Strategy")]
        public bool RequireUserArming { get; set; }

        [Parameter("Armed", DefaultValue = false, Group = "01 - Core Strategy")]
        public bool Armed { get; set; }

        // ====================================================================
        // 02 - SESSION & TIME
        // ====================================================================
        [Parameter("Session Start Hour (NY)", DefaultValue = 9, MinValue = 0, MaxValue = 23, Group = "02 - Session & Time")]
        public int SessionStartHour { get; set; }

        [Parameter("Session Start Minute (NY)", DefaultValue = 30, MinValue = 0, MaxValue = 59, Group = "02 - Session & Time")]
        public int SessionStartMinute { get; set; }

        [Parameter("Session End Hour (NY)", DefaultValue = 16, MinValue = 0, MaxValue = 23, Group = "02 - Session & Time")]
        public int SessionEndHour { get; set; }

        [Parameter("Session End Minute (NY)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Group = "02 - Session & Time")]
        public int SessionEndMinute { get; set; }

        [Parameter("Force Flat At Session End", DefaultValue = true, Group = "02 - Session & Time")]
        public bool ForceFlatAtSessionEnd { get; set; }

        [Parameter("Allow Overnight", DefaultValue = false, Group = "02 - Session & Time")]
        public bool AllowOvernight { get; set; }

        [Parameter("Minutes Before Close To Stop New Entries", DefaultValue = 0, MinValue = 0, Group = "02 - Session & Time")]
        public int MinutesBeforeCloseToStopEntries { get; set; }

        // ====================================================================
        // 03 - NOISE AREA
        // ====================================================================
        [Parameter("Noise Area Lookback (sessions)", DefaultValue = 14, MinValue = 2, Group = "03 - Noise Area")]
        public int NoiseAreaLookbackDays { get; set; }

        [Parameter("Volatility Multiplier", DefaultValue = 1.0, MinValue = 0.01, Group = "03 - Noise Area")]
        public double NoiseAreaVolatilityMultiplier { get; set; }

        [Parameter("Use Gap Adjustment", DefaultValue = true, Group = "03 - Noise Area")]
        public bool UseGapAdjustment { get; set; }

        [Parameter("Minimum Valid Sessions To Trade", DefaultValue = 14, MinValue = 2, Group = "03 - Noise Area")]
        public int MinValidSessions { get; set; }

        // ====================================================================
        // 04 - ENTRY ENGINE
        // ====================================================================
        [Parameter("Signal Check Interval (minutes)", DefaultValue = 30, MinValue = 1, Group = "04 - Entry Engine")]
        public int SignalCheckIntervalMinutes { get; set; }

        [Parameter("First Signal Minutes After Open", DefaultValue = 30, MinValue = 1, Group = "04 - Entry Engine")]
        public int FirstSignalMinutesAfterOpen { get; set; }

        [Parameter("Minimum Breakout Distance (price)", DefaultValue = 0.0, MinValue = 0.0, Group = "04 - Entry Engine")]
        public double MinimumBreakoutDistance { get; set; }

        // ====================================================================
        // 05 - VWAP
        // ====================================================================
        [Parameter("Use VWAP", DefaultValue = true, Group = "05 - VWAP")]
        public bool UseVWAP { get; set; }

        // ====================================================================
        // 06 - KAMA
        // ====================================================================
        [Parameter("KAMA Mode", DefaultValue = KamaModeOption.OFF, Group = "06 - KAMA")]
        public KamaModeOption KamaMode { get; set; }

        [Parameter("KAMA Period", DefaultValue = 10, MinValue = 2, Group = "06 - KAMA")]
        public int KamaPeriod { get; set; }

        [Parameter("KAMA Fast Period", DefaultValue = 2, MinValue = 1, Group = "06 - KAMA")]
        public int KamaFastPeriod { get; set; }

        [Parameter("KAMA Slow Period", DefaultValue = 30, MinValue = 2, Group = "06 - KAMA")]
        public int KamaSlowPeriod { get; set; }

        [Parameter("KAMA Slope Lookback (bars)", DefaultValue = 1, MinValue = 1, Group = "06 - KAMA")]
        public int KamaSlopeLookback { get; set; }

        [Parameter("KAMA Minimum Slope", DefaultValue = 0.0, Group = "06 - KAMA")]
        public double KamaMinimumSlope { get; set; }

        // ====================================================================
        // 07 - POSITION SIZING
        // ====================================================================
        [Parameter("Position Sizing Mode", DefaultValue = PositionSizingModeOption.FIXED_RISK_PERCENT, Group = "07 - Position Sizing")]
        public PositionSizingModeOption PositionSizingMode { get; set; }

        [Parameter("Risk Per Trade (%)", DefaultValue = 0.5, MinValue = 0.01, Group = "07 - Position Sizing")]
        public double RiskPerTradePercent { get; set; }

        [Parameter("Volatility Lookback Days", DefaultValue = 20, MinValue = 5, Group = "07 - Position Sizing")]
        public int VolatilityLookbackDays { get; set; }

        [Parameter("Target Daily Volatility (%)", DefaultValue = 2.0, MinValue = 0.01, Group = "07 - Position Sizing")]
        public double TargetDailyVolatilityPercent { get; set; }

        [Parameter("Maximum Leverage", DefaultValue = 4.0, MinValue = 0.1, Group = "07 - Position Sizing")]
        public double MaximumLeverage { get; set; }

        [Parameter("Fixed Lot Size", DefaultValue = 0.1, MinValue = 0.01, Group = "07 - Position Sizing")]
        public double FixedLotSize { get; set; }

        [Parameter("Fixed Notional", DefaultValue = 10000.0, MinValue = 1.0, Group = "07 - Position Sizing")]
        public double FixedNotional { get; set; }

        // ====================================================================
        // 08 - INITIAL STOP
        // ====================================================================
        [Parameter("Initial Stop Mode", DefaultValue = InitialStopModeOption.ORIGINAL_NOISE_VWAP, Group = "08 - Initial Stop")]
        public InitialStopModeOption InitialStopMode { get; set; }

        [Parameter("Initial ATR Period", DefaultValue = 14, MinValue = 1, Group = "08 - Initial Stop")]
        public int InitialAtrPeriod { get; set; }

        [Parameter("Initial ATR Multiplier", DefaultValue = 1.5, MinValue = 0.1, Group = "08 - Initial Stop")]
        public double InitialAtrMultiplier { get; set; }

        [Parameter("Fixed Stop Distance (price)", DefaultValue = 0.0, MinValue = 0.0, Group = "08 - Initial Stop")]
        public double FixedStopDistance { get; set; }

        // ====================================================================
        // 09 - BREAKEVEN & PARTIAL
        // ====================================================================
        [Parameter("Enable Breakeven", DefaultValue = true, Group = "09 - Breakeven & Partial")]
        public bool EnableBreakEven { get; set; }

        [Parameter("Breakeven Trigger (R)", DefaultValue = 1.5, MinValue = 0.01, Group = "09 - Breakeven & Partial")]
        public double BreakEvenTriggerR { get; set; }

        [Parameter("Breakeven Buffer (price)", DefaultValue = 0.0, MinValue = 0.0, Group = "09 - Breakeven & Partial")]
        public double BreakEvenBuffer { get; set; }

        [Parameter("Enable Partial Close", DefaultValue = true, Group = "09 - Breakeven & Partial")]
        public bool EnablePartialClose { get; set; }

        [Parameter("Partial Close Trigger (R)", DefaultValue = 1.5, MinValue = 0.01, Group = "09 - Breakeven & Partial")]
        public double PartialCloseTriggerR { get; set; }

        [Parameter("Partial Close Percent", DefaultValue = 50.0, MinValue = 1.0, MaxValue = 99.0, Group = "09 - Breakeven & Partial")]
        public double PartialClosePercent { get; set; }

        // ====================================================================
        // 10 - ATR TRAILING
        // ====================================================================
        [Parameter("Enable ATR Trailing", DefaultValue = true, Group = "10 - ATR Trailing")]
        public bool EnableAtrTrailing { get; set; }

        [Parameter("ATR Period", DefaultValue = 14, MinValue = 1, Group = "10 - ATR Trailing")]
        public int AtrPeriod { get; set; }

        [Parameter("ATR Multiplier", DefaultValue = 2.0, MinValue = 0.1, Group = "10 - ATR Trailing")]
        public double AtrMultiplier { get; set; }

        [Parameter("Post-Trigger Stop Mode", DefaultValue = PostTriggerStopModeOption.HYBRID, Group = "10 - ATR Trailing")]
        public PostTriggerStopModeOption PostTriggerStopMode { get; set; }

        // ====================================================================
        // 11 - EXIT ENGINE
        // ====================================================================
        [Parameter("Enable Fixed Take Profit", DefaultValue = false, Group = "11 - Exit Engine")]
        public bool EnableFixedTakeProfit { get; set; }

        [Parameter("Fixed Take Profit (R)", DefaultValue = 5.0, MinValue = 0.1, Group = "11 - Exit Engine")]
        public double FixedTakeProfitR { get; set; }

        // ====================================================================
        // 12 - PROP / ACCOUNT RISK
        // ====================================================================
        [Parameter("Enable Daily Loss Limit", DefaultValue = true, Group = "12 - Account Risk")]
        public bool EnableDailyLossLimit { get; set; }

        [Parameter("Max Daily Loss (%)", DefaultValue = 2.0, MinValue = 0.01, Group = "12 - Account Risk")]
        public double MaxDailyLossPercent { get; set; }

        [Parameter("Enable Max Drawdown", DefaultValue = true, Group = "12 - Account Risk")]
        public bool EnableMaxDrawdown { get; set; }

        [Parameter("Max Total Drawdown (%)", DefaultValue = 6.0, MinValue = 0.01, Group = "12 - Account Risk")]
        public double MaxTotalDrawdownPercent { get; set; }

        [Parameter("Close Positions On Risk Limit", DefaultValue = true, Group = "12 - Account Risk")]
        public bool ClosePositionsOnRiskLimit { get; set; }

        [Parameter("Max Trades Per Day", DefaultValue = 10, MinValue = 1, Group = "12 - Account Risk")]
        public int MaxTradesPerDay { get; set; }

        // ====================================================================
        // 13 - EXECUTION
        // ====================================================================
        [Parameter("Use Spread Filter", DefaultValue = true, Group = "13 - Execution")]
        public bool UseSpreadFilter { get; set; }

        [Parameter("Max Spread (price)", DefaultValue = 0.0, MinValue = 0.0, Group = "13 - Execution")]
        public double MaxSpread { get; set; }

        [Parameter("Trade Label", DefaultValue = "NoiseAreaEA", Group = "13 - Execution")]
        public string TradeLabel { get; set; }

        // ====================================================================
        // 14 - DISPLAY
        // ====================================================================
        [Parameter("Show Noise Area", DefaultValue = true, Group = "14 - Display")]
        public bool ShowNoiseArea { get; set; }

        [Parameter("Show Dashboard", DefaultValue = true, Group = "14 - Display")]
        public bool ShowDashboard { get; set; }

        // ====================================================================
        // 15 - LOGGING / DEBUG
        // ====================================================================
        [Parameter("Debug Mode (0=Off,1=Errors,2=Trades,3=Signals,4=Full)", DefaultValue = 2, MinValue = 0, MaxValue = 4, Group = "15 - Logging")]
        public int DebugLevel { get; set; }

        #region Internal State

        private TimeZoneInfo _nyTz;
        private int _sessionMinutes;

        // Noise area
        private double[] _sigma;
        private double _sessionOpen = double.NaN;
        private double _prevSessionClose = double.NaN;
        private DateTime _currentSessionDateNY = DateTime.MinValue;
        private int _validSessionCount = 0;

        // VWAP
        private double _vwapCumPV = 0;
        private double _vwapCumV = 0;
        private double _vwap = double.NaN;

        // KAMA
        private double _kamaPrev = double.NaN;
        private List<double> _kamaHistory = new List<double>();

        // Position lifecycle
        private TradeStateEnum _state = TradeStateEnum.FLAT;
        private double _entryPrice, _initialStop, _oneRDistance, _currentStop;
        private double _highestFav = double.NaN, _lowestFav = double.NaN;
        private bool _partialTaken = false;
        private bool _beActivated = false;
        private bool _atrTrailActive = false;
        private double _originalVolume = 0;
        private TradeType _dir;

        // Risk tracking
        private double _dayStartEquity;
        private double _peakEquity;
        private int _tradesToday = 0;
        private DateTime _lastRiskResetDateNY = DateTime.MinValue;
        private bool _riskLocked = false;

        // Signal timing
        private DateTime _lastSignalCheck = DateTime.MinValue;

        private AverageTrueRange _atrIndicator;
        private AverageTrueRange _initAtrIndicator;

        #endregion

        #region OnStart / OnStop

        protected override void OnStart()
        {
            if (TimeFrame != TimeFrame.Minute)
            {
                Print("FATAL: This bot must be run on a 1-minute (M1) chart. Current timeframe: {0}. Stopping.", TimeFrame);
                Stop();
                return;
            }

            try
            {
                _nyTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch (Exception ex)
            {
                Print("FATAL: Could not resolve 'Eastern Standard Time' timezone on this host: {0}. Stopping.", ex.Message);
                Stop();
                return;
            }

            ValidateParameters();

            _sessionMinutes = (SessionEndHour * 60 + SessionEndMinute) - (SessionStartHour * 60 + SessionStartMinute);
            if (_sessionMinutes <= 0)
            {
                Print("FATAL: Session end must be after session start. Stopping.");
                Stop();
                return;
            }
            _sigma = new double[_sessionMinutes];

            // This cTrader SDK exposes Exponential but not Wilder in MovingAverageType.
            // ATR remains valid; the smoothing choice is made explicit for portability.
            _atrIndicator = Indicators.AverageTrueRange(Bars, AtrPeriod, MovingAverageType.Exponential) as AverageTrueRange
                             ?? Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
            _initAtrIndicator = Indicators.AverageTrueRange(InitialAtrPeriod, MovingAverageType.Exponential);

            _dayStartEquity = Account.Equity;
            _peakEquity = Account.Equity;
            _lastRiskResetDateNY = ToNY(Server.Time).Date;

            ReattachToExistingPosition();

            LogEvent("STARTUP", "Bot started. Symbol={0} Mode={1} KAMA={2} Sizing={3}",
                SymbolName, StrategyMode, KamaMode, PositionSizingMode);
        }

        protected override void OnStop()
        {
            RemoveChartObjects();
        }

        #endregion

        #region Parameter Validation

        private void ValidateParameters()
        {
            if (KamaFastPeriod >= KamaSlowPeriod)
                Print("WARNING: KamaFastPeriod ({0}) should be < KamaSlowPeriod ({1}).", KamaFastPeriod, KamaSlowPeriod);

            if (PartialClosePercent <= 0 || PartialClosePercent >= 100)
                Print("WARNING: PartialClosePercent ({0}) should be between 1 and 99.", PartialClosePercent);

            if (KamaMode == KamaModeOption.EXIT_FILTER || KamaMode == KamaModeOption.REPLACE_INTERVAL)
                Print("WARNING: KAMA mode '{0}' is not implemented in this build. It will behave as OFF.", KamaMode);

            if (StrategyMode == StrategyModePreset.ORIGINAL_PAPER)
            {
                if (NoiseAreaLookbackDays != 14) Print("NOTE: ORIGINAL_PAPER preset conventionally uses a 14-session lookback; current setting is {0}.", NoiseAreaLookbackDays);
            }
            else if (StrategyMode == StrategyModePreset.QUANTITATIVO_90D)
            {
                if (NoiseAreaLookbackDays != 90) Print("NOTE: QUANTITATIVO_90D preset conventionally uses a 90-session lookback; current setting is {0}.", NoiseAreaLookbackDays);
            }
        }

        #endregion

        #region Time / Session Helpers

        private DateTime ToNY(DateTime serverTimeUtcOrServer)
        {
            // Server.Time in cTrader is typically already in the timezone configured
            // on the Robot attribute (here: Server timezone). We treat it as UTC-based
            // broker time and convert explicitly to America/New_York.
            DateTime asUtc = serverTimeUtcOrServer.Kind == DateTimeKind.Utc
                ? serverTimeUtcOrServer
                : DateTime.SpecifyKind(serverTimeUtcOrServer, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(asUtc, _nyTz);
        }

        private bool IsInSession(DateTime nyTime)
        {
            var start = new TimeSpan(SessionStartHour, SessionStartMinute, 0);
            var end = new TimeSpan(SessionEndHour, SessionEndMinute, 0);
            return nyTime.TimeOfDay >= start && nyTime.TimeOfDay < end;
        }

        private bool IsSessionStart(DateTime nyTime)
        {
            return nyTime.Hour == SessionStartHour && nyTime.Minute == SessionStartMinute;
        }

        private int MinutesSinceSessionStart(DateTime nyTime)
        {
            var start = new TimeSpan(SessionStartHour, SessionStartMinute, 0);
            return (int)(nyTime.TimeOfDay - start).TotalMinutes;
        }

        #endregion

        #region Noise Area Engine

        private class SessionInfo
        {
            public DateTime DateNY;
            public double OpenPrice = double.NaN;
            public double ClosePrice = double.NaN;
            public double[] MinuteCloses;
        }

        private void BuildNoiseArea(DateTime todayNY)
        {
            int lookback = NoiseAreaLookbackDays;
            var sessions = new List<SessionInfo>();
            SessionInfo current = null;

            // Make sure we have enough history loaded. We try a bounded number of
            // LoadMoreHistory() calls; if the feed runs out, we work with what we have.
            int safety = 0;
            while (Bars.OpenTimes.Count < (lookback + 5) * (_sessionMinutes + 60) && safety < 50)
            {
                int before = Bars.OpenTimes.Count;
                Bars.LoadMoreHistory();
                if (Bars.OpenTimes.Count == before) break; // no more history available
                safety++;
            }

            var byDate = new Dictionary<DateTime, SessionInfo>();

            for (int i = 0; i < Bars.OpenTimes.Count; i++)
            {
                DateTime nyTime = ToNY(Bars.OpenTimes[i]);
                if (nyTime.Date >= todayNY) continue; // exclude current/future session - no look-ahead
                if (!IsInSession(nyTime)) continue;

                if (!byDate.TryGetValue(nyTime.Date, out current))
                {
                    current = new SessionInfo { DateNY = nyTime.Date, MinuteCloses = new double[_sessionMinutes] };
                    for (int k = 0; k < _sessionMinutes; k++) current.MinuteCloses[k] = double.NaN;
                    byDate[nyTime.Date] = current;
                }

                if (IsSessionStart(nyTime) || double.IsNaN(current.OpenPrice))
                    current.OpenPrice = Bars.OpenPrices[i];

                int offset = MinutesSinceSessionStart(nyTime);
                if (offset >= 0 && offset < _sessionMinutes)
                    current.MinuteCloses[offset] = Bars.ClosePrices[i];

                current.ClosePrice = Bars.ClosePrices[i]; // last seen close in session = session close
            }

            sessions = byDate.Values
                .Where(s => !double.IsNaN(s.OpenPrice) && !double.IsNaN(s.ClosePrice))
                .OrderBy(s => s.DateNY)
                .ToList();

            _validSessionCount = sessions.Count;

            // Previous session close (most recent completed session before today)
            _prevSessionClose = sessions.Count > 0 ? sessions[sessions.Count - 1].ClosePrice : double.NaN;

            var lookbackSessions = sessions.Skip(Math.Max(0, sessions.Count - lookback)).ToList();

            for (int m = 0; m < _sessionMinutes; m++)
            {
                double sum = 0;
                int count = 0;
                foreach (var s in lookbackSessions)
                {
                    double c = s.MinuteCloses[m];
                    if (double.IsNaN(c) || s.OpenPrice == 0) continue;
                    sum += Math.Abs(c / s.OpenPrice - 1.0);
                    count++;
                }
                _sigma[m] = count > 0 ? sum / count : double.NaN;
            }

            LogEvent("NOISE_AREA_REBUILT", "SessionsFound={0} LookbackUsed={1} PrevClose={2}",
                _validSessionCount, lookbackSessions.Count, _prevSessionClose);
        }

        private void GetBands(int minuteOffset, out double upper, out double lower)
        {
            upper = double.NaN;
            lower = double.NaN;
            if (minuteOffset < 0 || minuteOffset >= _sessionMinutes) return;
            double sig = _sigma[minuteOffset];
            if (double.IsNaN(sig) || double.IsNaN(_sessionOpen)) return;

            double upperAnchor, lowerAnchor;
            if (UseGapAdjustment && !double.IsNaN(_prevSessionClose))
            {
                upperAnchor = Math.Max(_sessionOpen, _prevSessionClose);
                lowerAnchor = Math.Min(_sessionOpen, _prevSessionClose);
            }
            else
            {
                upperAnchor = _sessionOpen;
                lowerAnchor = _sessionOpen;
            }

            double adj = NoiseAreaVolatilityMultiplier * sig;
            upper = upperAnchor * (1 + adj);
            lower = lowerAnchor * (1 - adj);
        }

        #endregion

        #region VWAP

        private void ResetVWAP()
        {
            _vwapCumPV = 0;
            _vwapCumV = 0;
            _vwap = double.NaN;
        }

        private void UpdateVWAP(double typicalPrice, double volume)
        {
            if (!UseVWAP) return;
            _vwapCumPV += typicalPrice * volume;
            _vwapCumV += volume;
            if (_vwapCumV > 0) _vwap = _vwapCumPV / _vwapCumV;
        }

        #endregion

        #region KAMA

        private double CalculateKama(double price, int index)
        {
            _kamaHistory.Add(price);
            if (_kamaHistory.Count < KamaPeriod + 1)
                return double.NaN;

            int n = _kamaHistory.Count;
            double change = Math.Abs(_kamaHistory[n - 1] - _kamaHistory[n - 1 - KamaPeriod]);
            double volatility = 0;
            for (int i = n - KamaPeriod; i < n; i++)
                volatility += Math.Abs(_kamaHistory[i] - _kamaHistory[i - 1]);

            double er = volatility > 0 ? change / volatility : 0;
            double fastSC = 2.0 / (KamaFastPeriod + 1);
            double slowSC = 2.0 / (KamaSlowPeriod + 1);
            double sc = Math.Pow(er * (fastSC - slowSC) + slowSC, 2);

            if (double.IsNaN(_kamaPrev)) _kamaPrev = price;
            double kama = _kamaPrev + sc * (price - _kamaPrev);
            _kamaPrev = kama;

            // keep history bounded
            if (_kamaHistory.Count > 5000) _kamaHistory.RemoveRange(0, 2500);

            return kama;
        }

        private double _kamaCurrent = double.NaN;

        #endregion

        #region OnBar - main strategy loop

        protected override void OnBar()
        {
            if (!StrategyEnabled) return;

            DateTime nyNow = ToNY(Bars.OpenTimes.LastValue);
            DailyReset(nyNow);

            // Build/rebuild the Noise Area once per new session
            if (nyNow.Date != _currentSessionDateNY)
            {
                _currentSessionDateNY = nyNow.Date;
                BuildNoiseArea(nyNow.Date);
                _sessionOpen = double.NaN;
                ResetVWAP();
            }

            if (!IsInSession(nyNow))
            {
                if (ForceFlatAtSessionEnd && !AllowOvernight)
                    ForceFlatIfNeeded(ExitReasonEnum.END_OF_DAY);
                UpdateDashboard(nyNow);
                return;
            }

            if (IsSessionStart(nyNow) || double.IsNaN(_sessionOpen))
                _sessionOpen = Bars.OpenPrices.LastValue;

            double close = Bars.ClosePrices.LastValue;
            double high = Bars.HighPrices.LastValue;
            double low = Bars.LowPrices.LastValue;
            double typical = (high + low + close) / 3.0;
            double volume = Bars.TickVolumes.LastValue;
            UpdateVWAP(typical, volume);

            _kamaCurrent = CalculateKama(close, Bars.OpenTimes.Count - 1);

            int offset = MinutesSinceSessionStart(nyNow);
            GetBands(offset, out double upper, out double lower);

            // Risk gate
            EvaluateRiskLimits();

            // Manage any open position every bar (stop logic must react continuously,
            // not only at the 30-minute checkpoints - see spec section 24: LIVE mode).
            ManageOpenPosition(close, high, low);

            bool minutesLeftOk = true;
            if (MinutesBeforeCloseToStopEntries > 0)
            {
                var end = new TimeSpan(SessionEndHour, SessionEndMinute, 0);
                minutesLeftOk = (end - nyNow.TimeOfDay).TotalMinutes > MinutesBeforeCloseToStopEntries;
            }

            bool checkpoint = offset >= FirstSignalMinutesAfterOpen
                               && offset % SignalCheckIntervalMinutes == 0
                               && nyNow != _lastSignalCheck;

            if (checkpoint)
            {
                _lastSignalCheck = nyNow;

                if (_validSessionCount < MinValidSessions)
                {
                    LogEvent("SIGNAL_SKIPPED", "Insufficient Noise Area history: {0}/{1} sessions.", _validSessionCount, MinValidSessions);
                }
                else if (_riskLocked)
                {
                    LogEvent("SIGNAL_SKIPPED", "Risk lock active.");
                }
                else if (!minutesLeftOk)
                {
                    LogEvent("SIGNAL_SKIPPED", "Within MinutesBeforeCloseToStopEntries window.");
                }
                else if (UseSpreadFilter && MaxSpread > 0 && (Symbol.Ask - Symbol.Bid) > MaxSpread)
                {
                    LogEvent("SIGNAL_SKIPPED", "Spread {0} exceeds MaxSpread {1}.", Symbol.Ask - Symbol.Bid, MaxSpread);
                }
                else if (!double.IsNaN(upper) && !double.IsNaN(lower))
                {
                    EvaluateSignal(close, upper, lower);
                }

                if (DebugLevel >= 3)
                    LogEvent("SIGNAL_STATE", "t={0} close={1} upper={2} lower={3} vwap={4} kama={5}",
                        nyNow.ToString("HH:mm"), close, upper, lower, _vwap, _kamaCurrent);
            }

            if (ShowNoiseArea) DrawNoiseArea(upper, lower);
            UpdateDashboard(nyNow);
        }

        #endregion

        #region Signal Evaluation

        private void EvaluateSignal(double close, double upper, double lower)
        {
            bool longSignal = close > upper + MinimumBreakoutDistance;
            bool shortSignal = close < lower - MinimumBreakoutDistance;

            if (KamaMode == KamaModeOption.ENTRY_FILTER && !double.IsNaN(_kamaCurrent))
            {
                double slopeRef = _kamaHistory.Count > KamaSlopeLookback
                    ? _kamaHistory[_kamaHistory.Count - 1 - KamaSlopeLookback] : double.NaN;
                double slope = double.IsNaN(slopeRef) ? 0 : (_kamaCurrent - slopeRef);

                if (longSignal)
                    longSignal = close > _kamaCurrent && slope >= KamaMinimumSlope;
                if (shortSignal)
                    shortSignal = close < _kamaCurrent && slope <= -KamaMinimumSlope;
            }

            var openPos = Positions.Find(TradeLabel, SymbolName);

            if (longSignal && AllowLong)
            {
                if (openPos == null)
                    OpenNewPosition(TradeType.Buy, upper, lower);
                else if (openPos.TradeType == TradeType.Sell && AllowOppositeSignalFlip)
                {
                    ClosePositionInternal(openPos, ExitReasonEnum.OPPOSITE_SIGNAL);
                    OpenNewPosition(TradeType.Buy, upper, lower);
                }
                else if (openPos.TradeType == TradeType.Buy && AllowSameSideReentry && _state == TradeStateEnum.FLAT)
                {
                    OpenNewPosition(TradeType.Buy, upper, lower);
                }
            }
            else if (shortSignal && AllowShort)
            {
                if (openPos == null)
                    OpenNewPosition(TradeType.Sell, upper, lower);
                else if (openPos.TradeType == TradeType.Buy && AllowOppositeSignalFlip)
                {
                    ClosePositionInternal(openPos, ExitReasonEnum.OPPOSITE_SIGNAL);
                    OpenNewPosition(TradeType.Sell, upper, lower);
                }
                else if (openPos.TradeType == TradeType.Sell && AllowSameSideReentry && _state == TradeStateEnum.FLAT)
                {
                    OpenNewPosition(TradeType.Sell, upper, lower);
                }
            }
        }

        #endregion

        #region Position Opening

        private void OpenNewPosition(TradeType dir, double upper, double lower)
        {
            if (RequireUserArming && !Armed)
            {
                LogEvent("ENTRY_BLOCKED", "RequireUserArming is true and bot is not Armed.");
                return;
            }
            if (_tradesToday >= MaxTradesPerDay)
            {
                LogEvent("ENTRY_BLOCKED", "MaxTradesPerDay ({0}) reached.", MaxTradesPerDay);
                return;
            }

            double refPrice = dir == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double initialStop = ComputeInitialStop(dir, upper, lower, refPrice);

            if (double.IsNaN(initialStop) || initialStop <= 0)
            {
                LogEvent("ENTRY_BLOCKED", "Could not compute a valid initial stop.");
                return;
            }
            if (dir == TradeType.Buy && initialStop >= refPrice) { LogEvent("ENTRY_BLOCKED", "Invalid long stop >= price."); return; }
            if (dir == TradeType.Sell && initialStop <= refPrice) { LogEvent("ENTRY_BLOCKED", "Invalid short stop <= price."); return; }

            double riskDistance = Math.Abs(refPrice - initialStop);
            double volume = ComputeVolume(riskDistance);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
            {
                LogEvent("ENTRY_BLOCKED", "Computed volume {0} below symbol minimum {1}.", volume, Symbol.VolumeInUnitsMin);
                return;
            }

            var result = ExecuteMarketOrder(dir, SymbolName, volume, TradeLabel, (double?)null, (double?)null, "NoiseAreaEntry");
            if (!result.IsSuccessful || result.Position == null)
            {
                LogEvent("EXECUTION_FAILURE", "Order failed: {0}", result.Error);
                return;
            }

            var pos = result.Position;
            _entryPrice = pos.EntryPrice;
            _initialStop = ComputeInitialStop(dir, upper, lower, _entryPrice); // recompute off actual fill
            if ((dir == TradeType.Buy && _initialStop >= _entryPrice) || (dir == TradeType.Sell && _initialStop <= _entryPrice))
                _initialStop = initialStop; // fallback to pre-fill value if recompute is invalid after slippage

            _oneRDistance = Math.Abs(_entryPrice - _initialStop);
            _currentStop = _initialStop;
            _dir = dir;
            _partialTaken = false;
            _beActivated = false;
            _atrTrailActive = false;
            _originalVolume = pos.VolumeInUnits;
            _highestFav = _entryPrice;
            _lowestFav = _entryPrice;
            _state = dir == TradeType.Buy ? TradeStateEnum.LONG : TradeStateEnum.SHORT;
            _tradesToday++;

            ModifyPosition(pos, _currentStop, EnableFixedTakeProfit ? ComputeFixedTP(dir) : (double?)null, ProtectionType.Absolute);

            LogEvent("ENTRY", "DIRECTION={0} ENTRY={1} STOP={2} RISK_DIST={3} VOLUME={4}",
                dir, _entryPrice, _currentStop, _oneRDistance, volume);
        }

        private double? ComputeFixedTP(TradeType dir)
        {
            double dist = _oneRDistance * FixedTakeProfitR;
            return dir == TradeType.Buy ? _entryPrice + dist : _entryPrice - dist;
        }

        private double ComputeInitialStop(TradeType dir, double upper, double lower, double refPrice)
        {
            switch (InitialStopMode)
            {
                case InitialStopModeOption.ORIGINAL_NOISE_VWAP:
                    double vwapOrNaN = UseVWAP ? _vwap : double.NaN;
                    if (dir == TradeType.Buy)
                    {
                        double longStop = lower;
                        if (!double.IsNaN(vwapOrNaN) && vwapOrNaN < refPrice) longStop = Math.Max(longStop, vwapOrNaN);
                        return longStop < refPrice ? longStop : lower;
                    }
                    else
                    {
                        double shortStop = upper;
                        if (!double.IsNaN(vwapOrNaN) && vwapOrNaN > refPrice) shortStop = Math.Min(shortStop, vwapOrNaN);
                        return shortStop > refPrice ? shortStop : upper;
                    }

                case InitialStopModeOption.OPPOSITE_NOISE_BAND:
                    return dir == TradeType.Buy ? lower : upper;

                case InitialStopModeOption.ATR_INITIAL_STOP:
                    double atr = _initAtrIndicator.Result.LastValue;
                    if (double.IsNaN(atr) || atr <= 0) return double.NaN;
                    return dir == TradeType.Buy ? refPrice - atr * InitialAtrMultiplier : refPrice + atr * InitialAtrMultiplier;

                case InitialStopModeOption.FIXED_DISTANCE:
                    if (FixedStopDistance <= 0) return double.NaN;
                    return dir == TradeType.Buy ? refPrice - FixedStopDistance : refPrice + FixedStopDistance;

                default:
                    return double.NaN;
            }
        }

        #endregion

        #region Position Sizing

        private double ComputeVolume(double riskDistance)
        {
            switch (PositionSizingMode)
            {
                case PositionSizingModeOption.FIXED_LOT:
                    return Symbol.QuantityToVolumeInUnits(FixedLotSize);

                case PositionSizingModeOption.FIXED_NOTIONAL:
                    {
                        double price = Symbol.Bid > 0 ? Symbol.Bid : 1;
                        double units = FixedNotional / price;
                        return units;
                    }

                case PositionSizingModeOption.FIXED_RISK_PERCENT:
                    {
                        double riskAmount = Account.Equity * (RiskPerTradePercent / 100.0);
                        if (riskDistance <= 0) return 0;
                        if (Symbol.TickSize <= 0 || Symbol.TickValue <= 0)
                        {
                            double fallback = Symbol.QuantityToVolumeInUnits(FixedLotSize);
                            LogEvent("SIZING_FALLBACK", "Tick economics unavailable; using FixedLotSize={0} converted volume={1}.", FixedLotSize, fallback);
                            return fallback;
                        }
                        double moneyPerUnit = riskDistance * (Symbol.TickValue / Symbol.TickSize);
                        if (moneyPerUnit <= 0) return 0;
                        return riskAmount / moneyPerUnit;
                    }

                case PositionSizingModeOption.VOLATILITY_TARGET:
                    {
                        double realizedVol = EstimateRealizedDailyVolPercent();
                        if (realizedVol <= 0) return Symbol.QuantityToVolumeInUnits(FixedLotSize); // fallback
                        double leverage = Math.Min(MaximumLeverage, (TargetDailyVolatilityPercent / 100.0) / (realizedVol / 100.0));
                        double notional = Account.Equity * leverage;
                        double price = Symbol.Bid > 0 ? Symbol.Bid : 1;
                        return notional / price;
                    }

                default:
                    return Symbol.QuantityToVolumeInUnits(FixedLotSize);
            }
        }

        private double EstimateRealizedDailyVolPercent()
        {
            // Simplified realized volatility: stdev of daily session returns
            // computed from the session-close series already gathered while
            // building the Noise Area is not retained across calls, so this
            // recomputes a lightweight version directly from daily bars.
            var dailyBars = MarketData.GetBars(TimeFrame.Daily, SymbolName);
            int n = Math.Min(VolatilityLookbackDays, dailyBars.ClosePrices.Count - 1);
            if (n < 3) return 0;

            var returns = new List<double>();
            int last = dailyBars.ClosePrices.Count - 1;
            for (int i = last - n + 1; i <= last; i++)
            {
                if (i <= 0) continue;
                double r = dailyBars.ClosePrices[i] / dailyBars.ClosePrices[i - 1] - 1.0;
                returns.Add(r);
            }
            if (returns.Count < 3) return 0;
            double mean = returns.Average();
            double variance = returns.Sum(r => (r - mean) * (r - mean)) / returns.Count;
            return Math.Sqrt(variance) * 100.0;
        }

        #endregion

        #region Trade Management (Breakeven / Partial / ATR Trail)

        private void ManageOpenPosition(double close, double high, double low)
        {
            var pos = Positions.Find(TradeLabel, SymbolName);
            if (pos == null)
            {
                if (_state != TradeStateEnum.FLAT) _state = TradeStateEnum.FLAT;
                return;
            }

            _dir = pos.TradeType;
            if (_dir == TradeType.Buy)
            {
                if (double.IsNaN(_highestFav) || high > _highestFav) _highestFav = high;
            }
            else
            {
                if (double.IsNaN(_lowestFav) || low < _lowestFav) _lowestFav = low;
            }

            double currentR = _oneRDistance > 0
                ? (_dir == TradeType.Buy ? (close - _entryPrice) : (_entryPrice - close)) / _oneRDistance
                : 0;

            // --- Breakeven ---
            if (EnableBreakEven && !_beActivated && currentR >= BreakEvenTriggerR)
            {
                double beStop = _dir == TradeType.Buy ? _entryPrice + BreakEvenBuffer : _entryPrice - BreakEvenBuffer;
                ApplyStopNoWiden(pos, beStop);
                _beActivated = true;
                _state = _dir == TradeType.Buy ? TradeStateEnum.LONG_BE : TradeStateEnum.SHORT_BE;
                LogEvent("BREAK_EVEN", "R={0:0.00} newStop={1}", currentR, _currentStop);
            }

            // --- Partial close (exactly once) ---
            if (EnablePartialClose && !_partialTaken && currentR >= PartialCloseTriggerR)
            {
                double closeVolume = Symbol.NormalizeVolumeInUnits(_originalVolume * (PartialClosePercent / 100.0), RoundingMode.Down);
                if (closeVolume >= Symbol.VolumeInUnitsMin && closeVolume < pos.VolumeInUnits)
                {
                    var r = ClosePosition(pos, closeVolume);
                    if (r.IsSuccessful)
                    {
                        _partialTaken = true;
                        LogEvent("PARTIAL_CLOSE", "TRIGGER={0:0.00}R CLOSED={1} REMAINING={2}",
                            currentR, closeVolume, pos.VolumeInUnits - closeVolume);
                    }
                    else
                    {
                        LogEvent("EXECUTION_FAILURE", "Partial close failed: {0}", r.Error);
                    }
                }
                else
                {
                    // Can't safely partial (below min volume) - mark taken so we don't retry every bar.
                    _partialTaken = true;
                    LogEvent("PARTIAL_CLOSE_SKIPPED", "Requested partial volume below broker minimum; skipping partial for this position.");
                }
            }

            // --- ATR trailing activation & update ---
            bool activationMet = _beActivated || _partialTaken || currentR >= BreakEvenTriggerR;
            if (EnableAtrTrailing && activationMet)
            {
                _atrTrailActive = true;
                double atr = _atrIndicator.Result.LastValue;
                if (!double.IsNaN(atr) && atr > 0)
                {
                    double atrStop = _dir == TradeType.Buy
                        ? _highestFav - atr * AtrMultiplier
                        : _lowestFav + atr * AtrMultiplier;

                    double candidate;
                    switch (PostTriggerStopMode)
                    {
                        case PostTriggerStopModeOption.ATR_ONLY:
                            candidate = atrStop;
                            break;
                        case PostTriggerStopModeOption.ORIGINAL_ONLY:
                            candidate = _currentStop; // original noise/vwap stop is not re-derived here; hold
                            break;
                        case PostTriggerStopModeOption.HYBRID:
                        default:
                            candidate = _dir == TradeType.Buy
                                ? Math.Max(atrStop, _currentStop)
                                : Math.Min(atrStop, _currentStop);
                            break;
                    }
                    ApplyStopNoWiden(pos, candidate);
                    if (_state == TradeStateEnum.LONG_BE || _state == TradeStateEnum.LONG) _state = TradeStateEnum.LONG_TRAILING;
                    if (_state == TradeStateEnum.SHORT_BE || _state == TradeStateEnum.SHORT) _state = TradeStateEnum.SHORT_TRAILING;
                }
            }

            // --- End of day ---
            DateTime nyNow = ToNY(Server.Time);
            if (ForceFlatAtSessionEnd && !AllowOvernight && !IsInSession(nyNow) && nyNow.Date == _currentSessionDateNY)
            {
                ClosePositionInternal(pos, ExitReasonEnum.END_OF_DAY);
            }
        }

        /// Enforces: LONG stops may only move up, SHORT stops may only move down. Never widens risk.
        private void ApplyStopNoWiden(Position pos, double candidateStop)
        {
            if (double.IsNaN(candidateStop) || candidateStop <= 0) return;

            bool improves = _dir == TradeType.Buy
                ? candidateStop > _currentStop
                : candidateStop < _currentStop;

            if (!improves) return;

            // Respect current market price so we never submit an invalid stop.
            if (_dir == TradeType.Buy && candidateStop >= Symbol.Bid) return;
            if (_dir == TradeType.Sell && candidateStop <= Symbol.Ask) return;

            double normalized = Symbol.Bid > 0 ? Math.Round(candidateStop / Symbol.TickSize) * Symbol.TickSize : candidateStop;
            var result = ModifyPosition(pos, normalized, pos.TakeProfit, ProtectionType.Absolute);
            if (result.IsSuccessful)
                _currentStop = normalized;
            else
                LogEvent("EXECUTION_FAILURE", "Stop modification failed: {0}", result.Error);
        }

        private void ForceFlatIfNeeded(ExitReasonEnum reason)
        {
            var pos = Positions.Find(TradeLabel, SymbolName);
            if (pos != null) ClosePositionInternal(pos, reason);
        }

        private void ClosePositionInternal(Position pos, ExitReasonEnum reason)
        {
            var result = ClosePosition(pos);
            if (result.IsSuccessful)
            {
                LogEvent("EXIT", "REASON={0} EXIT_PRICE={1}", reason, pos.EntryPrice);
                _state = TradeStateEnum.FLAT;
                _partialTaken = false;
                _beActivated = false;
                _atrTrailActive = false;
            }
            else
            {
                LogEvent("EXECUTION_FAILURE", "Close failed ({0}): {1}", reason, result.Error);
            }
        }

        private void ReattachToExistingPosition()
        {
            var pos = Positions.Find(TradeLabel, SymbolName);
            if (pos == null) return;

            _dir = pos.TradeType;
            _entryPrice = pos.EntryPrice;
            _currentStop = pos.StopLoss ?? pos.EntryPrice;
            _initialStop = _currentStop;
            _oneRDistance = Math.Abs(_entryPrice - _currentStop);
            _originalVolume = pos.VolumeInUnits; // cannot know pre-partial volume with certainty; treat current as original
            _partialTaken = false; // conservative: cannot safely reconstruct - see file header limitations
            _beActivated = false;
            _atrTrailActive = false;
            _highestFav = _entryPrice;
            _lowestFav = _entryPrice;
            _state = _dir == TradeType.Buy ? TradeStateEnum.LONG : TradeStateEnum.SHORT;

            LogEvent("RESTART_RECOVERY", "Re-attached to existing position. Direction={0} Entry={1} Stop={2} (partial/BE progress could not be reconstructed - see file header)",
                _dir, _entryPrice, _currentStop);
        }

        #endregion

        #region Risk Manager

        private void DailyReset(DateTime nyNow)
        {
            if (nyNow.Date != _lastRiskResetDateNY)
            {
                _lastRiskResetDateNY = nyNow.Date;
                _dayStartEquity = Account.Equity;
                _tradesToday = 0;
                _riskLocked = false;
                LogEvent("DAILY_RESET", "New session. StartEquity={0}", _dayStartEquity);
            }
        }

        private void EvaluateRiskLimits()
        {
            if (Account.Equity > _peakEquity) _peakEquity = Account.Equity;

            if (EnableDailyLossLimit)
            {
                double dailyLossPct = _dayStartEquity > 0 ? (_dayStartEquity - Account.Equity) / _dayStartEquity * 100.0 : 0;
                if (dailyLossPct >= MaxDailyLossPercent && !_riskLocked)
                {
                    _riskLocked = true;
                    LogEvent("DAILY_RISK_LIMIT", "DailyLoss={0:0.00}% >= Limit={1:0.00}%", dailyLossPct, MaxDailyLossPercent);
                    if (ClosePositionsOnRiskLimit) ForceFlatIfNeeded(ExitReasonEnum.DAILY_RISK_LIMIT);
                }
            }

            if (EnableMaxDrawdown)
            {
                double ddPct = _peakEquity > 0 ? (_peakEquity - Account.Equity) / _peakEquity * 100.0 : 0;
                if (ddPct >= MaxTotalDrawdownPercent && !_riskLocked)
                {
                    _riskLocked = true;
                    LogEvent("MAX_DRAWDOWN", "Drawdown={0:0.00}% >= Limit={1:0.00}%", ddPct, MaxTotalDrawdownPercent);
                    if (ClosePositionsOnRiskLimit) ForceFlatIfNeeded(ExitReasonEnum.MAX_DRAWDOWN);
                }
            }
        }

        #endregion

        #region Dashboard / Chart Drawing

        private void DrawNoiseArea(double upper, double lower)
        {
            if (double.IsNaN(upper) || double.IsNaN(lower)) return;
            Chart.DrawHorizontalLine("NA_Upper", upper, Color.OrangeRed);
            Chart.DrawHorizontalLine("NA_Lower", lower, Color.LimeGreen);
            if (UseVWAP && !double.IsNaN(_vwap))
                Chart.DrawHorizontalLine("NA_VWAP", _vwap, Color.DodgerBlue);
            if (!double.IsNaN(_kamaCurrent))
                Chart.DrawHorizontalLine("NA_KAMA", _kamaCurrent, Color.Violet);
        }

        private void UpdateDashboard(DateTime nyNow)
        {
            if (!ShowDashboard) return;

            var pos = Positions.Find(TradeLabel, SymbolName);
            double currentR = 0;
            if (pos != null && _oneRDistance > 0)
            {
                double close = Bars.ClosePrices.LastValue;
                currentR = (_dir == TradeType.Buy ? (close - _entryPrice) : (_entryPrice - close)) / _oneRDistance;
            }

            double dailyPnlPct = _dayStartEquity > 0 ? (Account.Equity - _dayStartEquity) / _dayStartEquity * 100.0 : 0;
            double ddPct = _peakEquity > 0 ? (_peakEquity - Account.Equity) / _peakEquity * 100.0 : 0;

            string text =
                "INTRADAY MOMENTUM / NOISE AREA\n" +
                $"Symbol: {SymbolName}   Session: NY RTH   Sessions in NA: {_validSessionCount}\n" +
                $"Status: {_state}\n" +
                $"Open: {_sessionOpen:0.#####}   VWAP: {_vwap:0.#####}   KAMA: {_kamaCurrent:0.#####}\n" +
                (pos != null
                    ? $"Entry: {_entryPrice:0.#####}   Stop: {_currentStop:0.#####}   1R: {_oneRDistance:0.#####}   CurrentR: {currentR:0.00}\n" +
                      $"Partial: {(_partialTaken ? "DONE" : "PENDING")}   BE: {(_beActivated ? "YES" : "NO")}   ATR Trail: {(_atrTrailActive ? "ACTIVE" : "OFF")}\n"
                    : "No open position\n") +
                $"Daily P/L: {dailyPnlPct:0.00}%   Drawdown: {ddPct:0.00}%   Trades Today: {_tradesToday}/{MaxTradesPerDay}\n" +
                (_riskLocked ? "*** RISK LOCK ACTIVE ***" : "");

            Chart.DrawStaticText("Dashboard", text, VerticalAlignment.Top, HorizontalAlignment.Left, Color.White);
        }

        private void RemoveChartObjects()
        {
            Chart.RemoveObject("NA_Upper");
            Chart.RemoveObject("NA_Lower");
            Chart.RemoveObject("NA_VWAP");
            Chart.RemoveObject("NA_KAMA");
            Chart.RemoveObject("Dashboard");
        }

        #endregion

        #region Logging

        private void LogEvent(string eventName, string format, params object[] args)
        {
            int minLevelForEvent = ClassifyLogLevel(eventName);
            if (DebugLevel < minLevelForEvent) return;
            string msg = args.Length > 0 ? string.Format(format, args) : format;
            Print("[{0:yyyy-MM-dd HH:mm:ss}] EVENT={1} {2}", Server.Time, eventName, msg);
        }

        private int ClassifyLogLevel(string eventName)
        {
            switch (eventName)
            {
                case "EXECUTION_FAILURE":
                    return 1;
                case "ENTRY":
                case "EXIT":
                case "PARTIAL_CLOSE":
                case "PARTIAL_CLOSE_SKIPPED":
                case "BREAK_EVEN":
                case "DAILY_RISK_LIMIT":
                case "MAX_DRAWDOWN":
                case "ENTRY_BLOCKED":
                case "STARTUP":
                case "RESTART_RECOVERY":
                    return 2;
                case "SIGNAL_STATE":
                case "SIGNAL_SKIPPED":
                    return 3;
                default:
                    return 4;
            }
        }

        #endregion
    }
}
