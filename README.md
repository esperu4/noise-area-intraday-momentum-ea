# Intraday Momentum / Noise Area EA

Research-grade cTrader Automate implementation of the intraday momentum / Noise Area methodology, with the original strategy and experimental modules kept separate. **MT5 is intentionally deferred until the cTrader implementation is debugged and validated.**

## Current phase

This repository currently delivers the shared mathematical specification, a platform-independent C# research core, and the cTrader Automate adapter. The core can be tested without a trading terminal. The cTrader adapter keeps all broker API calls in `ExecutionManager`/`IntradayMomentumBot`.

This is research software, not a performance claim or investment advice. Run a 1-minute backtest and demo forward test before considering any live use.

## Layout

- `docs/STRATEGY_SPEC.md`: authoritative rules and pseudocode.
- `docs/Parameter_Guide.md`: parameter defaults and effects.
- `docs/Installation_and_Backtesting.md`: cTrader setup and validation procedure.
- `ctrader/Core`: deterministic strategy, indicators, risk, and trade-management logic.
- `ctrader/Models`: immutable-ish domain records and state enums.
- `ctrader/Utils`: price/volume math and structured logging.
- `ctrader/Robots/IntradayMomentumBot.cs`: cTrader Automate entry point.
- `tests`: deterministic acceptance tests for the platform-independent core.
- `research/parameter-presets`: reproducible JSON-style preset notes.

## Build status

The sandbox used to author this repository does not include the cTrader Automate SDK or the .NET compiler. The core is deliberately SDK-independent; compile the adapter in cTrader Algo using the current platform SDK. The test harness uses the available Python runtime and mirrors the same formulas for smoke validation. Any SDK-specific limitation is recorded in `docs/TEST_REPORT.md`.

## Safety defaults

The original preset uses 14 completed sessions, 30-minute checkpoints, KAMA off, custom ATR management off, force-flat at the session end, one strategy position per symbol, and no stop widening. Never enable live trading until symbol specifications, timezone/DST behavior, volume, spread, margin, and risk limits are verified.

## Attribution

The methodology originates from “Beat the Market: An Effective Intraday Momentum Strategy for S&P500 ETF” by Carlo Zarattini, Andrew Aziz, and Andrea Barbon. Quantitativo’s 90-session / volatility-target modifications are identified as research modifications; they are not attributed to this implementation.
