using System;
using cAlgo.API;

namespace IntradayMomentum.Core
{
    public sealed class ExecutionManager
    {
        private readonly Robot _robot;
        public ExecutionManager(Robot robot) { _robot = robot; }
        public TradeResult Open(string label, TradeType type, double volume, double stop, string comment)
        {
            try { return _robot.ExecuteMarketOrder(type, _robot.SymbolName, volume, label, stop, null, comment, false); }
            catch (Exception ex) { _robot.Print("EVENT=EXECUTION_FAILURE OP=OPEN ERROR={0}", ex.Message); return null; }
        }
        public bool Modify(Position position, double stop)
        {
            if (position == null) return false;
            bool valid = position.TradeType == TradeType.Buy ? stop < _robot.Symbol.Bid : stop > _robot.Symbol.Ask;
            if (!valid) { _robot.Print("EVENT=STOP_REJECTED REASON=INVALID_SIDE STOP={0}", stop); return false; }
            try { var result = _robot.ModifyPosition(position, stop, position.TakeProfit); return result != null && result.IsSuccessful; }
            catch (Exception ex) { _robot.Print("EVENT=EXECUTION_FAILURE OP=MODIFY ERROR={0}", ex.Message); return false; }
        }
        public bool Partial(Position position, double volume)
        {
            if (position == null || volume <= 0) return false;
            try { var result = _robot.ClosePosition(position, volume); return result != null && result.IsSuccessful; }
            catch (Exception ex) { _robot.Print("EVENT=EXECUTION_FAILURE OP=PARTIAL ERROR={0}", ex.Message); return false; }
        }
        public bool Close(Position position)
        {
            if (position == null) return false;
            try { var result = _robot.ClosePosition(position); return result != null && result.IsSuccessful; }
            catch (Exception ex) { _robot.Print("EVENT=EXECUTION_FAILURE OP=CLOSE ERROR={0}", ex.Message); return false; }
        }
    }
}
