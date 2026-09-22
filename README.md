# Intraday Momentum / Noise Area EA

Research-grade cTrader Automate and MetaTrader 5 implementations of the intraday momentum / Noise Area methodology, with the original strategy and experimental modules kept separate.

## Current phase

This repository currently delivers the shared mathematical specification, a platform-independent C# research core, the cTrader Automate adapter, and a self-contained MQL5 EA. The core can be tested without a trading terminal. The platform adapters keep broker calls at their execution boundary and emit explicit no-trade diagnostics.

This is research software, not a performance claim or investment advice. Run a 1-minute backtest and demo forward test before considering any live use.

## Layout

- `docs/STRATEGY_SPEC.md`: authoritative rules and pseudocode.
- `docs/Parameter_Guide.md`: parameter defaults and effects.
- `docs/Installation_and_Backtesting.md`: cTrader setup and validation procedure.
- `ctrader/Core`: deterministic strategy, indicators, risk, and trade-management logic.
- `ctrader/Models`: immutable-ish domain records and state enums.
- `ctrader/Utils`: price/volume math and structured logging.
- `ctrader/Robots/IntradayMomentumBot.cs`: cTrader Automate entry point.
- `ctrader/Robots/variants/ClaudeNoiseAreaBot.cs`: separate Claude-generated cTrader variant, compiler-fixed and audited without replacing the original.
- `mt5/IntradayMomentumEA.mq5`: self-contained MT5 Expert Advisor for MetaEditor.
- `pinescript/NoiseAreaIntradayMomentum.pine`: TradingView Pine Script v5 research strategy.
- `pinescript/NoiseAreaIntradayMomentum_Detailed.pine`: long-form, heavily documented Pine companion with the same executable behavior.
- `tests`: deterministic acceptance tests for the platform-independent core.
- `research/parameter-presets`: reproducible JSON-style preset notes.

## Build status

The sandbox used to author this repository does not include the cTrader Automate SDK, .NET compiler, MetaEditor, or an MT5 terminal. For a single-file cTrader import, copy the complete contents of `ctrader/Robots/IntradayMomentumBot.cs` into a new cBot. For MT5, copy `mt5/IntradayMomentumEA.mq5` into `MQL5/Experts/` and compile it in MetaEditor. The test harness uses the available Python runtime and mirrors the shared formulas for smoke validation. Terminal-specific limitations are recorded in `docs/TEST_REPORT.md`.

## Safety defaults

The original preset uses 14 completed sessions, 30-minute checkpoints, KAMA off, custom ATR management off, force-flat at the session end, one strategy position per symbol, and no stop widening. Never enable live trading until symbol specifications, timezone/DST behavior, volume, spread, margin, and risk limits are verified.

## Attribution

The methodology originates from “Beat the Market: An Effective Intraday Momentum Strategy for S&P500 ETF” by Carlo Zarattini, Andrew Aziz, and Andrea Barbon. Quantitativo’s 90-session / volatility-target modifications are identified as research modifications; they are not attributed to this implementation.
