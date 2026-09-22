using System;
using System.Collections.Generic;
using System.Linq;
using IntradayMomentum.Models;

namespace IntradayMomentum.Core
{
    public sealed class NoiseAreaEngine
    {
        public NoiseArea Calculate(double sessionOpen, double previousClose, int minute, double multiplier, IReadOnlyList<IReadOnlyList<double>> history)
        {
            if (sessionOpen <= 0 || history == null || history.Count == 0 || history.Any(x => x == null || x.Count <= minute)) return new NoiseArea(false, 0, 0, 0);
            double sigma = history.Average(x => Math.Abs(x[minute] / x[0] - 1.0));
            double upperAnchor = Math.Max(sessionOpen, previousClose), lowerAnchor = Math.Min(sessionOpen, previousClose);
            return new NoiseArea(true, sigma, upperAnchor * (1 + multiplier * sigma), lowerAnchor * (1 - multiplier * sigma));
        }
    }

    public sealed class VwapEngine
    {
        private double _pv, _volume;
        public void Reset() { _pv = 0; _volume = 0; }
        public double Update(Bar bar) { _pv += ((bar.High + bar.Low + bar.Close) / 3.0) * Math.Max(0, bar.Volume); _volume += Math.Max(0, bar.Volume); return Value; }
        public double Value => _volume <= 0 ? 0 : _pv / _volume;
    }

    public sealed class KamaEngine
    {
        private readonly List<double> _prices = new List<double>();
        public double Value { get; private set; }
        public double Slope { get; private set; }
        public double Update(double price, int period, int fast, int slow, int slopeLookback)
        {
            if (period < 1 || fast < 1 || slow <= fast) throw new ArgumentException("Invalid KAMA parameters");
            _prices.Add(price);
            if (_prices.Count == 1) { Value = price; return Value; }
            if (_prices.Count <= period) { Value = price; return Value; }
            double change = Math.Abs(price - _prices[_prices.Count - 1 - period]);
            double volatility = 0;
            for (int i = _prices.Count - period; i < _prices.Count; i++) volatility += Math.Abs(_prices[i] - _prices[i - 1]);
            double er = volatility <= 1e-12 ? 0 : change / volatility;
            double fastSc = 2.0 / (fast + 1), slowSc = 2.0 / (slow + 1);
            double sc = Math.Pow(er * (fastSc - slowSc) + slowSc, 2);
            double previous = Value; Value = previous + sc * (price - previous);
            Slope = _prices.Count <= slopeLookback ? 0 : (Value - previous);
            return Value;
        }
    }

    public sealed class AtrEngine
    {
        private readonly Queue<double> _trs = new Queue<double>();
        private double _previousClose;
        public double Value { get; private set; }
        public double Update(Bar bar, int period)
        {
            double tr = _trs.Count == 0 ? bar.High - bar.Low : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _previousClose), Math.Abs(bar.Low - _previousClose)));
            _previousClose = bar.Close; _trs.Enqueue(tr); while (_trs.Count > Math.Max(1, period)) _trs.Dequeue(); Value = _trs.Average(); return Value;
        }
    }
}
