# MT5 No-Trade Audit

## Conclusion

The absence of trades was not safely attributable to a single high-level filter without the Experts log. A source audit found that the EA could legitimately reject every entry for several independent reasons, and it also found a material historical-session ordering defect that could invalidate or distort the Noise Area. The highest-probability causes are insufficient prior sessions, incorrect server-to-New-York conversion in the tester, and zero or unusable symbol tick economics. The latest revision addresses those blockers and emits a reason for each rejection.

## Execution path

A trade can occur only when all of the following gates pass in order: a new completed M1 bar is available; the converted bar timestamp is inside the configured New York session; the strategy is enabled; the daily equity lock is clear; the daily trade count is below its cap; spread is within the point limit; the completed session history contains at least the configured lookback; the timestamp is an exact signal checkpoint after the opening bar; the completed close is strictly above the upper band or below the lower band; the optional KAMA filter agrees; volume is valid; the stop is on the safe side of the market; and the trade server returns a successful execution retcode.

The log now identifies the first failed gate. `EVENT=NO_TRADE REASON=WARMUP_OR_MISSING_HISTORY` means the strategy did not reach signal evaluation. `EVENT=NO_SIGNAL` means the strategy reached a checkpoint but the close was inside the band. `EVENT=ENTRY_REJECTED REASON=INVALID_VOLUME_OR_STOP` means the signal existed but broker economics or stop validation rejected the order. `EVENT=TRADE_OPERATION OP=OPEN OK=false` means the order reached the broker and the retcode/description is the decisive cause.

## Findings and corrections

| Finding | Impact | Correction |
|---|---|---|
| The original source used hard-coded 09:30/16:00 values in several calculations | Custom session inputs did not actually control the Noise Area, VWAP, or event gate | All session calculations now use the configured start/end parameters |
| The historical session loop ran in reverse and assigned the first encountered bar as session open | Depending on `CopyRates` ordering, the open could become the session’s final bar and the previous close could become its first bar, distorting bands or preventing valid history | Historical bars are now traversed chronologically; session opens and final closes are preserved correctly |
| DST used “last Sunday” logic instead of U.S. rules | March and November conversions could be wrong, moving all bars outside the expected 09:30–16:00 New York window | Conversion now uses the second Sunday in March at 07:00 UTC and first Sunday in November at 06:00 UTC |
| Automatic server-offset detection may be unavailable or unreliable in Strategy Tester | Bars could be shifted by hours, causing session and warmup gates to reject everything | Manual UTC offset remains available; the start warning and diagnostics identify the active setup |
| Some CFD/index symbols expose zero or unusual tick-value metadata | Fixed-risk sizing returned zero volume and silently prevented an order attempt | Sizing now falls back to `OrderCalcProfit` when tick size/value is unavailable |
| Filling mode was not explicitly selected from the symbol | Brokers can reject otherwise-valid market orders with invalid filling mode | `trade.SetTypeFillingBySymbol(_Symbol)` is called during initialization |
| Own-position detection used symbol selection rather than an account-mode-aware scan | Hedging accounts could select a manual or unrelated position and block/modify the wrong position | The EA now scans positions by symbol and magic number |
| A valid opposite signal could be suppressed whenever any own position existed | Reversal behavior was unreachable | Opposite signals close the owned position and then attempt the replacement order when enabled |

## Most likely interpretations of the earlier behavior

If the Experts log showed only initialization and no subsequent diagnostics, the EA was probably not receiving usable M1 history/ticks or was attached to a non-M1 workflow. If it showed repeated warmup messages, the tester date range did not contain enough completed New York sessions after timezone conversion. If it showed spread messages, the configured `Maximum Spread Points` was below the symbol’s current spread; points are not pips and differ by symbol digits. If it showed invalid volume, fixed-risk sizing could not calculate broker economics or the calculated amount was below the minimum volume. If it showed `NO_SIGNAL`, the bot was functioning and simply had no strict band breakout at the scheduled checkpoint. If it showed `OPEN OK=false`, the broker retcode description—not the boolean method return—is the actual execution diagnosis.

## Verification procedure

Compile the latest `mt5/IntradayMomentumEA.mq5` in MetaEditor, attach it to an M1 chart, and run the Strategy Tester with at least 34 completed New York sessions for the 14-session setting. Use a symbol with normal tick-value metadata first, such as a broker-provided major FX symbol, before testing an index CFD. Keep `Diagnostics=true`, inspect the Experts log, and record the first recurring `EVENT=NO_TRADE`, `EVENT=ENTRY_REJECTED`, or `EVENT=TRADE_OPERATION` message. For a successful order, require `EVENT=TRADE_OPERATION OP=OPEN OK=true` and a successful retcode. Then confirm the position has the expected magic number, stop, volume, and later management events.

This audit does not claim that a trade must occur on arbitrary data. A correct momentum strategy can produce zero trades when no checkpoint close breaks the band. It does establish that the revised EA no longer hides which gate prevented execution.
