# Research Presets

## Original Paper

Noise lookback 14; multiplier 1.0; interval 30 minutes; target daily volatility 2%; maximum leverage 4x; KAMA off; custom ATR manager off; original VWAP/band exit on; force flat on.

## Quantitativo ES/NQ

Noise lookback 90; multiplier 1.0; interval 30 minutes; target daily volatility 3%; maximum leverage 8x; KAMA off; custom ATR manager off; original VWAP/band exit on; force flat on.

## KAMA Research

Noise lookback 90; 30-minute interval; KAMA period 10, fast 2, slow 30; KAMA entry filter on; target volatility 3%; maximum leverage 8x. This is an experiment, not an optimization claim.

## Custom ATR Management

Noise lookback 90; KAMA off; initial stop original Noise/VWAP; break-even and partial trigger 1.5R; partial 50%; ATR period 14; multiplier 2.0; force flat on; fixed take profit off.

## Prop Safe

Fixed-risk sizing; risk per trade 0.25%; KAMA off; ATR manager on; 1.5R BE/partial; partial 50%; ATR multiplier 2.0; daily loss, drawdown, maximum trades, and concurrent position limits set explicitly by the account owner.
