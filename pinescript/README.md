# TradingView Pine Script Strategy

`NoiseAreaIntradayMomentum.pine` is a separate Pine Script v5 strategy implementation of the shared Noise Area methodology. Open TradingView Pine Editor, paste the file, save it as a new strategy, and add it to a **1-minute chart**. Keep the chart timezone independent of the strategy because the script uses `America/New_York` for the session.

For the long-form version, use `NoiseAreaIntradayMomentum_Detailed.pine`. It contains the same executable logic with an expanded design/audit preamble, platform-parity notes, no-trade diagnosis guidance, and a module-by-module review contract modeled after the detailed cTrader variant. Use only one of the two Pine files on a chart at a time.

The strategy stores completed New York sessions in memory, calculates the gap-adjusted Noise Area from the configured prior-session lookback, resets VWAP at 09:30 New York, checks completed-bar breakouts at the configured interval, and supports optional KAMA entry filtering, fixed-risk sizing, break-even, partial close, ATR trailing, opposite-signal flips, daily loss lock, and end-of-day flattening.

Use at least 34 completed New York sessions for the default 14-session lookback and preferably much more for research. The first valid checkpoint is 10:00 New York by default; the opening bar is never used as an entry checkpoint.

TradingView limitations are material. Pine strategies are backtest/order-simulation scripts rather than broker execution adapters. Commission and slippage are strategy-level settings, tick volume may not equal exchange volume, and broker-side partial-close/restart state cannot be reconstructed exactly. In `RESEARCH` execution mode, stop management is evaluated only at scheduled checkpoints. In `LIVE` mode, the script updates the simulated stop every bar. These modes must not be compared as if they were identical execution models.

Before using results, verify symbol session data, TradingView volume source, commission, slippage, position sizing units, and the strategy report. This script is for research and controlled paper testing, not an assertion of live profitability.
