# MT5 Expert Advisor

`IntradayMomentumEA.mq5` is a self-contained MetaTrader 5 EA. Copy it to `MQL5/Experts/`, open it in MetaEditor, compile, and attach it to a 1-minute chart. It uses the active chart symbol and magic number rather than hard-coded ES/NQ assumptions.

The default is enabled for controlled demo/backtest execution, but it is not a live-trading approval. Before running, verify the broker server UTC offset, tick size/value, volume step, spread units, trading hours, and New York session conversion. The EA requires at least 14 completed prior New York sessions by default.

Inspect the Experts log. `EVENT=START` confirms initialization; `EVENT=NO_TRADE` identifies a disabled, warmup, spread, daily-loss, or maximum-trades gate; `EVENT=NO_SIGNAL` reports the bands; and `EVENT=TRADE_OPERATION` reports the trade-server retcode and description. Do not treat an order method returning `true` as execution success unless the retcode is also successful.

The EA supports netting and hedging partial-close paths. It manages only positions matching the configured symbol and magic number. Run the Strategy Tester first, then demo forward-test before considering any live use.
