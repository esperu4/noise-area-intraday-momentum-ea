# Claude cTrader Variant Audit

The Claude-generated cTrader bot is preserved beside the original implementation at `ctrader/Robots/variants/ClaudeNoiseAreaBot.cs`. It is a separate cBot class and should be imported into its own cTrader cBot project or compiled as a distinct variant. The original `ctrader/Robots/IntradayMomentumBot.cs` remains unchanged.

## Compiler corrections

The incompatible `TimeZoneSelection.Server` attribute was replaced with `TimeZones.UTC`, and the timezone conversion continues explicitly into the Windows `Eastern Standard Time` zone. `MovingAverageType.WeightedMoving` was replaced with the supported `MovingAverageType.Wilder` for ATR initialization. The market-order call now uses explicit nullable `double` arguments to select the legacy-compatible overload without ambiguity. Both stop modifications now pass `ProtectionType.Absolute`, removing the obsolete overload warning. The unused `_kamaSlopeRef` field was removed. The class was renamed to `ClaudeNoiseAreaBot` so it cannot collide with another `IntradayMomentumBot` if both source files are present in one project.

## Runtime blockers found and corrected

The original Claude source had an initial-stop inversion in `ORIGINAL_NOISE_VWAP`: long entries used the upper band and short entries used the lower band. This made the long stop sit above the entry and the short stop sit below the entry, causing `OpenNewPosition` to reject both directions with `Invalid long stop >= price` or `Invalid short stop <= price`. The corrected logic uses the opposite Noise Area side and incorporates VWAP only when it remains on the valid side of the market.

The original fixed-risk sizing returned zero whenever `Symbol.TickSize` or `Symbol.TickValue` was missing or zero. That is common on some index and CFD feeds and silently blocks every entry at the minimum-volume check. The variant now logs `SIZING_FALLBACK` and uses the configured fixed-lot volume as a controlled fallback. This fallback is deliberately visible in the log and should be reviewed before live use because its money risk cannot be inferred from missing broker metadata.

The original variant also treated the incoming time as UTC while declaring the robot `Server` timezone. That could shift all bars outside the New York session and leave the bot permanently in `SIGNAL_SKIPPED` or outside-session behavior. The corrected UTC robot attribute makes the conversion assumption explicit and consistent.

## Remaining conditions that can legitimately prevent trades

The bot requires a 1-minute chart, `Strategy Enabled=true`, at least `MinValidSessions` completed sessions, a checkpoint after the first signal interval, a strict close beyond the band, valid direction permissions, no daily or total drawdown lock, no maximum-trades lock, acceptable spread when the filter is enabled, a valid stop, and volume at or above the symbol minimum. `RequireUserArming` is false by default, so it is not a default blocker. The dashboard and event log should be inspected rather than assuming a strategy failure.

The sandbox does not include the cTrader Automate SDK, so MetaEditor/cTrader compilation cannot be performed here. The reported API identifiers have been removed, all shared Python tests pass, and the source is ready for compilation in cTrader. The first cTrader build log after this revision remains the authoritative SDK validation.
