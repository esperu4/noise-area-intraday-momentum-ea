# cTrader Installation and Backtesting

## Install

Open cTrader Algo and create a C# cBot. Copy the complete contents of `ctrader/Robots/IntradayMomentumBot.cs` into the cBot editor. This is the standalone import and does not require adding the other files. The `ctrader/Core` and `ctrader/Models` files are retained as the modular research source for a multi-file project. Build against the current cTrader Automate API. Attach `IntradayMomentumBot` to a 1-minute chart and choose the chart symbol; do not hard-code ES, NQ, or CFD names.

Before the first run, verify the symbol tick size/value, volume units and step, trading hours, broker timezone, spread, and the effective New York session shown in the dashboard. Start with the Original Paper preset and a demo account. The safety default is `Strategy Enabled = false`; set it to `true` for a controlled backtest. The bot now prints `EVENT=NO_TRADE` reasons for disabled state, insufficient completed sessions, spread, or daily risk lock. With the default 14-session lookback, the test range must contain at least 14 completed New York sessions before the first eligible signal.

## Backtest

Use 1-minute data with at least `NoiseAreaLookbackDays + 20` completed sessions; 120 sessions is recommended for the 90-session configuration. Run a non-visual baseline, then a visual backtest and inspect upper/lower bands, session VWAP, entries, stops, partial marker, ATR trail, and end-of-day flattening. Record data range, spread, slippage, commission, timezone, preset, and every parameter change.

The original research mode evaluates signal and research stops at scheduled checkpoints. Live mode may maintain broker-side protective stops and therefore is not identical to a checkpoint-only historical simulation. Do not compare these modes without labeling the difference.

## MT5 install and execution check

Copy `mt5/IntradayMomentumEA.mq5` to the terminal data folder at `MQL5/Experts/`, open it in MetaEditor, compile, and attach it to a **1-minute** chart. The EA is self-contained and uses `CTrade`; it checks trade-server retcodes after opening, closing, partial closing, and stop modification. It supports both netting and hedging account behavior, uses the configured magic number, and prints `EVENT=NO_TRADE`, `EVENT=NO_SIGNAL`, and `EVENT=TRADE_OPERATION` records.

For the first demo test, leave the default `Strategy Enabled = true`, use a symbol with at least 14 completed New York sessions in the tester, and inspect the Experts log. A successful execution must show `EVENT=TRADE_OPERATION OP=OPEN OK=true` with a successful retcode; an attempted order with an invalid volume, stop, spread, warmup, or risk state is logged instead of being silently ignored. Confirm the broker’s server UTC offset when automatic detection is unreliable and use `Manual Server UTC Offset`.

## Research matrix

Run the original 14-session / 2% / 4x preset first. Separately compare 90-session / 3% / 8x, KAMA off/on, and custom ATR management off/on. Do not optimize automatically or call a parameter set optimal. Use out-of-sample and walk-forward periods after the baseline is reproducible.

## Deterministic acceptance checks

The core tests cover: no entry at 09:30; strict band breakouts; same-side re-entry; opposite flips; force-flat; exact 1.5R BE/partial/trailing activation; one-time partial; no stop widening; restart-safe lifecycle state; daily-loss and drawdown locks; invalid size/stop rejection; and 14 versus 90 session differences. Cross-platform equivalence will be added after the MT5 implementation.

## Live/demo safety

This software is not production-ready merely because it compiles. Demo-forward test first. Keep `StrategyEnabled=false` until the dashboard confirms warmup, timezone, valid Noise Area, valid stop, volume, margin, spread, and risk settings. Never use martingale, grid, averaging down, or undocumented filters.
