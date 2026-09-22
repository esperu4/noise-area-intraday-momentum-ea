# cTrader Parameter Guide

The cTrader bot exposes grouped parameters so experiments can be changed without editing strategy formulas. The following table records the initial research-safe defaults.

| Parameter | Default | Purpose and effects |
|---|---:|---|
| StrategyEnabled | false | Master arm. Enabling permits orders; it does not bypass warmup, spread, stop, or risk checks. |
| AllowLong / AllowShort | true | Direction gates. Disabling a side changes signal execution, not band construction. |
| AllowSameSideReentry | true | Allows a later breakout after a same-side position has closed. |
| AllowOppositeFlip | true | Closes the own position and reverses on an opposite confirmed signal. |
| SessionStartHour/Minute | 09:30 | New York session start; changing it changes open, VWAP reset, and minute alignment. |
| SessionEndHour/Minute | 16:00 | Session close and force-flat boundary. |
| NoiseLookback | 14 | Prior completed sessions used for Sigma. Increasing it smooths bands and delays warmup; 90 is the Quantitativo experiment. |
| NoiseMultiplier | 1.0 | Scales Sigma. Increasing widens bands and reduces breakouts; decreasing narrows them. |
| SignalInterval | 30 | Scheduled checkpoint spacing. It changes signal count and timing. |
| MinimumBreakoutDistance | 0 | Optional extra price distance beyond a band; increasing filters marginal breaks. |
| UseVwap | true | Enables session VWAP for original stop calculations. Tick-volume symbols must be labeled accordingly. |
| KamaMode | Off | Optional research filter. Off leaves baseline signals unchanged. |
| KamaPeriod/Fast/Slow | 10/2/30 | KAMA responsiveness. These affect signals only when KAMA mode is enabled. |
| RiskPerTrade | 0.25% | Fixed-risk sizing input. It changes risk and volume, not signal generation. |
| InitialStop | OriginalNoiseVwap | Initial stop formula. Every stop is validated for correct side and never widened. |
| EnableBreakEven | true | Enables one-way move to entry at the R trigger. |
| BreakEvenTriggerR | 1.5 | R threshold for BE, partial, and ATR activation. Increasing delays protection. |
| PartialClosePercent | 50 | One-time reduction at trigger. It must leave valid broker volume. |
| EnableAtrTrail | true | Enables best-favorable-excursion ATR ratchet after trigger. |
| AtrPeriod / AtrMultiplier | 14/2.0 | ATR trail sensitivity. Higher multiplier gives more room; it can never loosen the stop. |
| ForceFlat | true | Closes strategy-owned positions at session end. Keep enabled for original research. |
| DailyLossLimit | 2.0% | Account-level entry lock threshold. Production integrations must calculate baseline from realized and floating P&L. |
| MaximumSpread | 5 pips | Spread gate. Lower values reject more entries. |
| TradeLabel | NoiseAreaEA | cTrader ownership identifier. Never reuse it for unrelated strategies or manual trades. |
| Debug | false | When enabled, emits signal and execution diagnostics; it does not alter decisions. |

The cTrader adapter also validates lookback, multiplier, signal interval, KAMA ordering, partial percentage, ATR settings, risk percentage, and leverage before startup. Symbol tick size, tick value, volume minimum, maximum, and step are read from the active symbol rather than hard-coded.
