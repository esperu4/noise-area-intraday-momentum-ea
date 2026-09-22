# Shared Strategy Specification

## Scope and layer separation

The implementation has five independently switchable layers: **A** original Noise Area momentum, **B** Quantitativo lookback and volatility-target research settings, **C** optional KAMA, **D** optional custom trade management, and **E** account/prop risk controls. Layer C/D/E must not silently alter Layer A when disabled.

## Data contract

Use completed 1-minute observations. A session is identified in the configured timezone (default `America/New_York`), from 09:30 through 16:00, with DST rules applied. The first valid bar at session start is `SessionOpen`; the final valid price before session end is `PreviousSessionClose` for the next session. Missing bars invalidate the affected session rather than being silently filled.

For each prior completed session `d` and minute-of-session `t`:

```text
Move[d,t] = abs(HistoricalClose[d,t] / HistoricalSessionOpen[d] - 1)
Sigma[t]   = mean(Move[d,t]) over the prior N completed sessions
Upper[t]   = max(SessionOpen, PreviousSessionClose) * (1 + Multiplier * Sigma[t])
Lower[t]   = min(SessionOpen, PreviousSessionClose) * (1 - Multiplier * Sigma[t])
```

Today's price is never included in today's `Sigma`. `N=14` is the research-faithful default; `N=90` is the Quantitativo experiment. Gap adjustment may be disabled only explicitly, in which case both anchors equal the current session open.

## Signal timing

The default signal interval is 30 minutes. No signal is permitted at 09:30. At each valid checkpoint after the first completed observation, use the completed observation close. Long requires `Close > Upper + MinimumBreakoutDistance`; short requires `Close < Lower - MinimumBreakoutDistance`. A value inside the band is no signal. Same-side re-entry is allowed by default. An opposite signal closes and reverses only when `AllowOppositeFlip` is enabled. One strategy position per symbol is the default.

## Indicators and exits

Session VWAP resets at session open and is `sum(price*volume)/sum(volume)`, using real volume where available and tick volume otherwise (the dashboard must identify tick-volume VWAP). The original research stop is separate from custom management: long `max(Upper,VWAP)` and short `min(Lower,VWAP)`; the older band-only mode is also supported. Force-flat at session end is enabled by default.

KAMA uses the standard efficiency ratio and squared smoothing constant. It is OFF by default. In entry-filter mode, long additionally requires `Price > KAMA` and slope >= threshold; short requires the inverse. Continuous replacement is experimental and never presented as the original strategy.

## Custom trade manager

At the actual fill, `RiskDistance=abs(ActualFill-InitialStop)`, and `1R=RiskDistance`. At the configured R trigger (default 1.5), move the stop to breakeven plus optional buffer, close the configured percentage once (default 50%), and activate ATR trailing. Long trailing uses `HighestFavorable - ATR*Multiplier`; short uses `LowestFavorable + ATR*Multiplier`. Long stops may only increase; short stops may only decrease. Invalid stops are rejected rather than widening risk.

## Position sizing and risk

The fixed-risk mode computes volume from account equity, risk percent, stop distance, and symbol tick economics, then clamps and floors to broker volume step. Volatility targeting is an explicit alternative, with target daily volatility and maximum leverage caps. Daily loss, maximum drawdown, spread, maximum trades, and concurrent-position controls block new entries and may close positions according to configuration.

## State machine

```text
SESSION_CLOSED -> FLAT -> SIGNAL_PENDING -> LONG|SHORT
LONG -> LONG_BE -> LONG_TRAILING -> FLAT
SHORT -> SHORT_BE -> SHORT_TRAILING -> FLAT
any active state -> FLAT by stop, opposite signal, risk limit, or end of day
any state -> RISK_LOCK when a configured account limit is breached
```

## Deterministic pseudocode

```text
On completed 1-minute bar:
  convert timestamp to session timezone using DST-aware rules
  reset daily/session counters at a new session
  if session closed: force-flat if configured; return
  update session open, previous close, historical session cache, VWAP, KAMA, ATR
  if warmup or Sigma unavailable: log DATA_NOT_READY; return
  calculate Sigma[t], anchors, Upper[t], Lower[t]
  update favorable excursion and custom trade manager on every completed bar
  if risk limit: transition RISK_LOCK; close if configured; return
  if now is a valid configured checkpoint and close-confirmation is enabled:
    signal = LONG if close > Upper + distance; SHORT if close < Lower - distance; otherwise NONE
    apply KAMA filter only when enabled
    if signal and no position: validate spread, size, stop, and duplicate timestamp; submit once
    if opposite signal and flip enabled: close own position with reason OPPOSITE_SIGNAL; submit replacement once
    if same-side signal after a completed lifecycle and re-entry enabled: submit once
    evaluate research stop at the same checkpoint in RESEARCH mode
  if LIVE custom management:
    calculate R from actual fill; apply BE/partial once; ratchet ATR stop without widening
  if force-flat and at/end of session: close own positions with END_OF_DAY
```

## Rounding and tolerance

Prices are normalized to symbol digits; volume is floored to the broker step and never below minimum. Comparisons use a documented epsilon of `1e-10` in normalized price units. Cross-platform ports must use these same rules.
