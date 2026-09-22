# cTrader Installation and Backtesting

## Install

Open cTrader Algo and create a C# cBot. Copy the complete contents of `ctrader/Robots/IntradayMomentumBot.cs` into the cBot editor. This is the standalone import and does not require adding the other files. The `ctrader/Core` and `ctrader/Models` files are retained as the modular research source for a multi-file project. Build against the current cTrader Automate API. Attach `IntradayMomentumBot` to a 1-minute chart and choose the chart symbol; do not hard-code ES, NQ, or CFD names.

Before the first run, verify the symbol tick size/value, volume units and step, trading hours, broker timezone, spread, and the effective New York session shown in the dashboard. Start with the Original Paper preset and a demo account.

## Backtest

Use 1-minute data with at least `NoiseAreaLookbackDays + 20` completed sessions; 120 sessions is recommended for the 90-session configuration. Run a non-visual baseline, then a visual backtest and inspect upper/lower bands, session VWAP, entries, stops, partial marker, ATR trail, and end-of-day flattening. Record data range, spread, slippage, commission, timezone, preset, and every parameter change.

The original research mode evaluates signal and research stops at scheduled checkpoints. Live mode may maintain broker-side protective stops and therefore is not identical to a checkpoint-only historical simulation. Do not compare these modes without labeling the difference.

## Research matrix

Run the original 14-session / 2% / 4x preset first. Separately compare 90-session / 3% / 8x, KAMA off/on, and custom ATR management off/on. Do not optimize automatically or call a parameter set optimal. Use out-of-sample and walk-forward periods after the baseline is reproducible.

## Deterministic acceptance checks

The core tests cover: no entry at 09:30; strict band breakouts; same-side re-entry; opposite flips; force-flat; exact 1.5R BE/partial/trailing activation; one-time partial; no stop widening; restart-safe lifecycle state; daily-loss and drawdown locks; invalid size/stop rejection; and 14 versus 90 session differences. Cross-platform equivalence will be added after the MT5 implementation.

## Live/demo safety

This software is not production-ready merely because it compiles. Demo-forward test first. Keep `StrategyEnabled=false` until the dashboard confirms warmup, timezone, valid Noise Area, valid stop, volume, margin, spread, and risk settings. Never use martingale, grid, averaging down, or undocumented filters.
