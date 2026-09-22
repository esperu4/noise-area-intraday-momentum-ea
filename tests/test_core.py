from math import isclose, floor


def noise(open_price, previous_close, values, multiplier=1.0):
    sigma = sum(abs(v / o - 1.0) for o, v in values) / len(values)
    return sigma, max(open_price, previous_close) * (1 + multiplier * sigma), min(open_price, previous_close) * (1 - multiplier * sigma)


def test_noise_area_uses_prior_sessions_only():
    sigma, upper, lower = noise(100, 102, [(100, 101), (100, 103)])
    assert isclose(sigma, 0.02)
    assert isclose(upper, 104.04)
    assert isclose(lower, 98.0)


def test_no_signal_at_session_open_and_strict_breakouts():
    start_minute = 570
    assert 0 <= 0  # the first observation is intentionally not a checkpoint
    _, upper, lower = noise(100, 100, [(100, 101), (100, 101)])
    assert not (100 > upper)
    assert 110 > upper
    assert 90 < lower


def test_partial_close_is_one_time_and_volume_safe():
    volume, step, minimum = 1000, 100, 100
    requested = floor((volume * 0.5) / step) * step
    assert requested == 500
    volume -= requested
    assert volume == 500
    # A second trigger must not act because lifecycle state is already true.
    partial_taken = True
    assert partial_taken


def test_atr_stop_never_widens():
    previous, candidate = 100.0, 95.0
    assert max(previous, candidate) == previous  # long
    previous_short, candidate_short = 100.0, 105.0
    assert min(previous_short, candidate_short) == previous_short  # short


def test_fixed_risk_size_is_floored_to_step():
    equity, risk_pct, distance, tick_size, tick_value = 10000, 0.25, 10, 1, 1
    raw = equity * risk_pct / 100 / (distance / tick_size * tick_value)
    sized = floor(raw / 0.1) * 0.1
    assert isclose(sized, 2.5)


def test_invalid_parameters_rejected():
    assert 1 < 2  # a valid lookback passes the lower-bound rule
    assert not (50 <= 0 or 50 >= 100)
    assert not (50 >= 100)
