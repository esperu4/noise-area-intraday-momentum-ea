# cTrader Phase Test Report

## Deterministic core checks

The repository includes `tests/test_core.py`, which can be run with `python3 -m pytest -q` when pytest is available. The cases cover prior-session-only Sigma, gap-adjusted bands, no opening signal, strict long/short breakouts, one-time volume-safe partial close, no-widening ATR stops, step-rounded fixed-risk sizing, and invalid-parameter rejection.

## Acceptance coverage

| Acceptance test | cTrader phase status | Evidence |
|---|---|---|
| No trade at 09:30 | PASS in core contract | `SignalEngine` rejects minute 0 and the spec requires completed observations. |
| Strict long/short band break | PASS in core contract | `SignalEngine` uses `>` and `<`. |
| Same-side re-entry | PARTIAL | Adapter has the parameter and no artificial opposite-band gate; full broker lifecycle test remains. |
| Opposite flip | PARTIAL | Adapter closes own label before opening replacement; requires terminal simulation. |
| Session-close flat | PARTIAL | `OnBar` force-flat path is present; requires cTrader runtime. |
| Exact 1.5R BE/partial/ATR | PASS in core contract | `TradeManager` activates all at the configured trigger. |
| One-time partial / restart safety | PARTIAL | In-memory lifecycle guard is present; persistent metadata reconstruction is pending runtime integration. |
| ATR never widens | PASS in core contract | Directional max/min ratchet. |
| Daily loss / drawdown locks | PASS in core unit | `RiskManager` blocks after thresholds; account-baseline adapter integration remains. |
| Invalid size / stop rejection | PASS in core contract | `PositionSizer` returns zero and adapter rejects invalid-side stops. |
| 14 vs 90 sessions | PASS in core contract | Lookback is configurable and warmup requires enough sessions. |
| KAMA OFF equivalence | PASS in core contract | Entry filter is only applied for `KamaMode.EntryFilter`. |
| cTrader/MT5 equivalence | NOT RUN | MT5 source is now present; identical-data terminal comparison remains to be run. |

## Environment limitation

The authoring sandbox does not provide the cTrader Automate SDK, MetaEditor, an MQL5 compiler, or a trading terminal. Therefore SDK-backed compilation and visual backtests cannot honestly be marked PASS here. The self-contained MT5 adapter checks trade-server retcodes and handles netting/hedging partial-close paths, but it still requires MetaEditor compilation and Strategy Tester/demo verification on the user’s broker. Persistent restart reconstruction, broker-specific stop/freeze validation, and terminal-level visual behavior remain explicit runtime checks. This is an explicit known limitation, not a production-readiness claim.
