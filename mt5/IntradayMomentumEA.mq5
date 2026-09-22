#property strict
#property version   "1.00"
#property description "Research Noise Area intraday momentum EA; test on demo first."

#include <Trade/Trade.mqh>
CTrade trade;

enum ENUM_KAMA_MODE { KAMA_OFF, KAMA_ENTRY_FILTER };
enum ENUM_SIZING_MODE { SIZING_FIXED_RISK, SIZING_FIXED_VOLUME };

input group "01 - CORE STRATEGY"
input bool InpStrategyEnabled=true;
input bool InpAllowLong=true;
input bool InpAllowShort=true;
input bool InpAllowOppositeFlip=true;
input long InpMagicNumber=26092201;
input string InpTradeComment="NoiseAreaEA";

input group "02 - SESSION & TIME"
input int InpSessionStartHour=9;
input int InpSessionStartMinute=30;
input int InpSessionEndHour=16;
input int InpSessionEndMinute=0;
input bool InpUseAutoServerUtcOffset=true;
input double InpManualServerUtcOffset=0.0;
input bool InpUseUsDst=true;
input bool InpForceFlat=true;

input group "03 - NOISE AREA"
input int InpNoiseLookback=14;
input double InpNoiseMultiplier=1.0;
input int InpWarmupExtraSessions=20;

input group "04 - ENTRY ENGINE"
input int InpSignalIntervalMinutes=30;
input double InpMinimumBreakoutDistance=0.0;
input bool InpUseClosedBar=true;

input group "05 - VWAP"
input bool InpUseVWAP=true;

input group "06 - KAMA"
input ENUM_KAMA_MODE InpKamaMode=KAMA_OFF;
input int InpKamaPeriod=10;
input int InpKamaFast=2;
input int InpKamaSlow=30;

input group "07 - POSITION SIZING"
input ENUM_SIZING_MODE InpSizingMode=SIZING_FIXED_RISK;
input double InpRiskPercent=0.25;
input double InpFixedVolume=0.10;

input group "09 - BREAKEVEN & PARTIAL"
input bool InpEnableBreakEven=true;
input double InpBreakEvenTriggerR=1.5;
input bool InpEnablePartial=true;
input double InpPartialPercent=50.0;

input group "10 - ATR TRAILING"
input bool InpEnableATRTrail=true;
input int InpATRPeriod=14;
input double InpATRMultiplier=2.0;

input group "12 - PROP / ACCOUNT RISK"
input bool InpEnableDailyLossLimit=true;
input double InpDailyLossLimitPercent=2.0;
input int InpMaximumTradesPerDay=10;

input group "13 - EXECUTION"
input int InpMaximumSpreadPoints=50;
input int InpDeviationPoints=20;

input group "15 - LOGGING / DEBUG"
input bool InpDiagnostics=true;

struct NoiseBand { bool valid; double sigma,upper,lower; };
struct ManagedPosition { bool valid,partialTaken,beActivated,trailActivated; ulong ticket; ENUM_POSITION_TYPE type; double entry,stop,risk,best,worst; };
ManagedPosition managed;
datetime lastBar=0,lastSignal=0; double dayBaseline=0; int dayKey=-1, tradesToday=0; bool riskLock=false;

void Log(string text) { if(InpDiagnostics) Print(text); }
double TickNormalize(double p) { double tick=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE); if(tick<=0) tick=_Point; return NormalizeDouble(MathRound(p/tick)*tick,(int)SymbolInfoInteger(_Symbol,SYMBOL_DIGITS)); }
double VolumeFloor(double v) { double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP), minv=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN), maxv=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX); if(step<=0) return 0; v=MathFloor(v/step+1e-9)*step; if(v<minv) return 0; return MathMin(v,maxv); }
int DateKey(datetime t) { MqlDateTime x; TimeToStruct(t,x); return x.year*10000+x.mon*100+x.day; }
int MinutesOf(datetime t) { MqlDateTime x; TimeToStruct(t,x); return x.hour*60+x.min; }
datetime LastSunday(int year,int month) { MqlDateTime x; x.year=year; x.mon=month+1; if(month==12){x.year=year+1;x.mon=1;} x.day=1;x.hour=0;x.min=0;x.sec=0; datetime first=StructToTime(x)-86400; MqlDateTime y;TimeToStruct(first,y); return first-(y.day_of_week*86400); }
bool UsDst(datetime utc) { MqlDateTime x;TimeToStruct(utc,x); if(x.mon<3||x.mon>11)return false; if(x.mon>3&&x.mon<11)return true; int sunday=LastSunday(x.year,x.mon); MqlDateTime s;TimeToStruct(sunday,s); if(x.mon==3)return x.day>s.day || (x.day==s.day&&x.hour>=7); return x.day<s.day || (x.day==s.day&&x.hour<6); }
datetime ServerToUtc(datetime server) { if(InpUseAutoServerUtcOffset){ datetime nowServer=TimeTradeServer(),nowGmt=TimeGMT(); if(nowServer>0&&nowGmt>0)return server-(nowServer-nowGmt); } return server-(int)(InpManualServerUtcOffset*3600); }
datetime ServerToNewYork(datetime server) { datetime utc=ServerToUtc(server); return utc-(UsDst(utc)&&InpUseUsDst?4:5)*3600; }

int OnInit()
{
 if(InpNoiseLookback<2||InpNoiseMultiplier<=0||InpSignalIntervalMinutes<=0||InpKamaFast<1||InpKamaSlow<=InpKamaFast||InpATRPeriod<1||InpATRMultiplier<=0||InpRiskPercent<=0||InpPartialPercent<=0||InpPartialPercent>=100){Print("INIT_FAILED=INVALID_PARAMETERS");return INIT_PARAMETERS_INCORRECT;}
 trade.SetExpertMagicNumber(InpMagicNumber); trade.SetDeviationInPoints(InpDeviationPoints); dayKey=DateKey(ServerToNewYork(TimeCurrent())); dayBaseline=AccountInfoDouble(ACCOUNT_EQUITY); managed.valid=false;
 PrintFormat("EVENT=START ENABLED=%s SYMBOL=%s LOOKBACK=%d INTERVAL=%d SESSION=09:30-16:00_NEW_YORK MAGIC=%I64d",InpStrategyEnabled?"true":"false",_Symbol,InpNoiseLookback,InpSignalIntervalMinutes,InpMagicNumber); Print("WARNING=AUTOMATED_STRATEGY VERIFY_TIMEZONE_SYMBOL_VOLUME_SPREAD_RISK"); return INIT_SUCCEEDED;
}

bool IsOwnPosition()
{
 for(int i=PositionsTotal()-1;i>=0;i--){ulong ticket=PositionGetTicket(i);if(ticket==0||!PositionSelectByTicket(ticket))continue;if(PositionGetString(POSITION_SYMBOL)==_Symbol&&(long)PositionGetInteger(POSITION_MAGIC)==InpMagicNumber)return true;}return false;
}
void RecoverPosition()
{
 if(!IsOwnPosition()){managed.valid=false;return;} managed.valid=true; managed.ticket=(ulong)PositionGetInteger(POSITION_TICKET); managed.type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE); managed.entry=PositionGetDouble(POSITION_PRICE_OPEN); managed.stop=PositionGetDouble(POSITION_SL); if(managed.risk<=0&&managed.stop>0)managed.risk=MathAbs(managed.entry-managed.stop); if(managed.best<=0)managed.best=managed.entry; if(managed.worst<=0)managed.worst=managed.entry; if(StringFind(PositionGetString(POSITION_COMMENT),"PT1")>=0)managed.partialTaken=true;
}
int CountCompletedSessions(datetime currentNy)
{
 MqlRates r[]; int n=CopyRates(_Symbol,PERIOD_M1,0,MathMax(5000,(InpNoiseLookback+InpWarmupExtraSessions)*500),r); if(n<=0)return 0; bool seen[]; ArrayResize(seen,n); ArrayInitialize(seen,false); int count=0; for(int i=0;i<n;i++){datetime ny=ServerToNewYork(r[i].time);if(DateKey(ny)>=DateKey(currentNy)||MinutesOf(ny)<570||MinutesOf(ny)>=960)continue;int key=DateKey(ny);bool found=false;for(int j=0;j<i;j++){datetime p=ServerToNewYork(r[j].time);if(DateKey(p)==key){found=true;break;}}if(!found)count++;}return count;
}
NoiseBand BuildNoise(datetime currentNy)
{
 NoiseBand out;out.valid=false;out.sigma=0;out.upper=0;out.lower=0; MqlRates r[]; int n=CopyRates(_Symbol,PERIOD_M1,0,MathMax(5000,(InpNoiseLookback+InpWarmupExtraSessions)*500),r); if(n<=0)return out;
 int curMinute=MinutesOf(currentNy)-570; if(curMinute<=0)return out; int keys[];double opens[],closes[];ArrayResize(keys,0);ArrayResize(opens,0);ArrayResize(closes,0);
 for(int i=n-1;i>=0;i--){datetime ny=ServerToNewYork(r[i].time);int key=DateKey(ny),m=MinutesOf(ny);if(key>=DateKey(currentNy)||m<570||m>=960)continue;int pos=-1;for(int j=0;j<ArraySize(keys);j++)if(keys[j]==key){pos=j;break;}if(pos<0){pos=ArraySize(keys);ArrayResize(keys,pos+1);ArrayResize(opens,pos+1);ArrayResize(closes,pos+1);keys[pos]=key;opens[pos]=r[i].open;closes[pos]=r[i].close;}else closes[pos]=r[i].close;}
 if(ArraySize(keys)<InpNoiseLookback)return out; double sum=0;int used=0; double prevClose=0; for(int j=0;j<InpNoiseLookback;j++){if(j==0)prevClose=closes[j];int target=-1;for(int i=0;i<n;i++){datetime ny=ServerToNewYork(r[i].time);if(DateKey(ny)==keys[j]&&MinutesOf(ny)-570==curMinute){target=i;break;}}if(target>=0){sum+=MathAbs(r[target].close/opens[j]-1.0);used++;}}
 if(used<InpNoiseLookback)return out; double sessionOpen=0;for(int i=0;i<n;i++){datetime ny=ServerToNewYork(r[i].time);if(DateKey(ny)==DateKey(currentNy)&&MinutesOf(ny)>=570&&MinutesOf(ny)<960){sessionOpen=r[i].open;break;}}if(sessionOpen<=0)return out;out.sigma=sum/used;double ua=MathMax(sessionOpen,prevClose),la=MathMin(sessionOpen,prevClose);out.upper=ua*(1+InpNoiseMultiplier*out.sigma);out.lower=la*(1-InpNoiseMultiplier*out.sigma);out.valid=true;return out;
}

double SessionVWAP(datetime currentNy)
{
 MqlRates r[];int n=CopyRates(_Symbol,PERIOD_M1,0,1000,r);double pv=0,v=0;for(int i=0;i<n;i++){datetime ny=ServerToNewYork(r[i].time);if(DateKey(ny)!=DateKey(currentNy)||MinutesOf(ny)<570||MinutesOf(ny)>=960)continue;double q=MathMax(0.0,r[i].tick_volume);pv+=((r[i].high+r[i].low+r[i].close)/3.0)*q;v+=q;}return v>0?pv/v:0;
}
double AtrValue()
{
 MqlRates r[];int n=CopyRates(_Symbol,PERIOD_M1,1,InpATRPeriod+1,r);if(n<2)return 0;double sum=0;for(int i=1;i<n;i++)sum+=MathMax(r[i].high-r[i].low,MathMax(MathAbs(r[i].high-r[i-1].close),MathAbs(r[i].low-r[i-1].close)));return sum/(n-1);
}
double KAMAValue()
{
 MqlRates r[];int n=CopyRates(_Symbol,PERIOD_M1,1,InpKamaPeriod+InpKamaSlow+5,r);if(n<InpKamaPeriod+2)return 0;double value=r[n-1].close;for(int i=n-2;i>=InpKamaPeriod;i--){double change=MathAbs(r[i].close-r[i+InpKamaPeriod].close),vol=0;for(int j=i;j<i+InpKamaPeriod;j++)vol+=MathAbs(r[j].close-r[j+1].close);double er=vol>1e-12?change/vol:0,fast=2.0/(InpKamaFast+1),slow=2.0/(InpKamaSlow+1),sc=MathPow(er*(fast-slow)+slow,2);value=value+sc*(r[i].close-value);}return value;
}

double CalculateVolume(double entry,double stop)
{
 if(InpSizingMode==SIZING_FIXED_VOLUME)return VolumeFloor(InpFixedVolume);double tickSize=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),tickValue=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE);double cashRisk=AccountInfoDouble(ACCOUNT_EQUITY)*InpRiskPercent/100.0,lossPerLot=MathAbs(entry-stop)/tickSize*tickValue;return lossPerLot>0?VolumeFloor(cashRisk/lossPerLot):0;
}
bool TradeOk(uint rc) { return rc==TRADE_RETCODE_DONE||rc==TRADE_RETCODE_DONE_PARTIAL||rc==TRADE_RETCODE_PLACED; }
void PrintTradeResult(string op,bool ok){PrintFormat("EVENT=TRADE_OPERATION OP=%s OK=%s RETCODE=%u DESC=%s",op,ok?"true":"false",trade.ResultRetcode(),trade.ResultRetcodeDescription());}

bool CloseOwn()
{ if(!IsOwnPosition())return true; ulong ticket=(ulong)PositionGetInteger(POSITION_TICKET);bool ok=trade.PositionClose(ticket);PrintTradeResult("CLOSE",ok&&TradeOk(trade.ResultRetcode()));return ok&&TradeOk(trade.ResultRetcode()); }
bool PartialClose(double volume)
{ if(!IsOwnPosition())return false;ulong ticket=(ulong)PositionGetInteger(POSITION_TICKET);ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);bool hedging=AccountInfoInteger(ACCOUNT_MARGIN_MODE)==ACCOUNT_MARGIN_MODE_RETAIL_HEDGING;bool ok=hedging?trade.PositionClosePartial(ticket,volume):(type==POSITION_TYPE_BUY?trade.Sell(volume,_Symbol,0,0,0,InpTradeComment+" PT1"):trade.Buy(volume,_Symbol,0,0,0,InpTradeComment+" PT1"));PrintTradeResult("PARTIAL",ok&&TradeOk(trade.ResultRetcode()));return ok&&TradeOk(trade.ResultRetcode()); }
bool ModifyOwn(double stop)
{ if(!IsOwnPosition())return false;ulong ticket=(ulong)PositionGetInteger(POSITION_TICKET);stop=TickNormalize(stop);ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);double bid=SymbolInfoDouble(_Symbol,SYMBOL_BID),ask=SymbolInfoDouble(_Symbol,SYMBOL_ASK);if((type==POSITION_TYPE_BUY&&stop>=bid)||(type==POSITION_TYPE_SELL&&stop<=ask))return false;bool ok=trade.PositionModify(ticket,stop,0);PrintTradeResult("MODIFY_SL",ok&&TradeOk(trade.ResultRetcode()));return ok&&TradeOk(trade.ResultRetcode()); }

void ManagePosition()
{
 if(!IsOwnPosition()||!managed.valid)return;double price=managed.type==POSITION_TYPE_BUY?SymbolInfoDouble(_Symbol,SYMBOL_BID):SymbolInfoDouble(_Symbol,SYMBOL_ASK);double high=iHigh(_Symbol,PERIOD_M1,1),low=iLow(_Symbol,PERIOD_M1,1);if(managed.type==POSITION_TYPE_BUY)managed.best=MathMax(managed.best,high);else managed.worst=managed.worst<=0?low:MathMin(managed.worst,low);if(managed.risk<=0)return;double r=managed.type==POSITION_TYPE_BUY?(price-managed.entry)/managed.risk:(managed.entry-price)/managed.risk;if(r+1e-10<InpBreakEvenTriggerR)return;double stop=managed.stop;bool changed=false;if(InpEnableBreakEven&&!managed.beActivated){stop=managed.entry;managed.beActivated=true;changed=true;}if(InpEnablePartial&&!managed.partialTaken){double current=PositionGetDouble(POSITION_VOLUME),part=VolumeFloor(current*InpPartialPercent/100.0);if(part>0&&current-part>=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN)&&PartialClose(part))managed.partialTaken=true;}if(InpEnableATRTrail){double atr=AtrValue();if(atr>0){double candidate=managed.type==POSITION_TYPE_BUY?managed.best-atr*InpATRMultiplier:managed.worst+atr*InpATRMultiplier;stop=managed.type==POSITION_TYPE_BUY?MathMax(stop,candidate):MathMin(stop,candidate);managed.trailActivated=true;changed=true;}}if(managed.type==POSITION_TYPE_BUY)stop=MathMax(stop,managed.stop);else stop=MathMin(stop,managed.stop);if(changed&&MathAbs(stop-managed.stop)>SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE))if(ModifyOwn(stop))managed.stop=stop;
}

void OpenSignal(ENUM_POSITION_TYPE type,double price,NoiseBand &band,datetime signalTime)
{
 if(IsOwnPosition()){ENUM_POSITION_TYPE existing=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);if(existing==type)return;if(!InpAllowOppositeFlip||!CloseOwn())return;managed.valid=false;}if(type==POSITION_TYPE_BUY&&!InpAllowLong)return;if(type==POSITION_TYPE_SELL&&!InpAllowShort)return;double vwap=InpUseVWAP?SessionVWAP(ServerToNewYork(TimeCurrent())):0;double stop=type==POSITION_TYPE_BUY?band.lower:band.upper;if(vwap>0)stop=type==POSITION_TYPE_BUY?MathMin(stop,vwap):MathMax(stop,vwap);stop=TickNormalize(stop);double volume=CalculateVolume(price,stop);double bid=SymbolInfoDouble(_Symbol,SYMBOL_BID),ask=SymbolInfoDouble(_Symbol,SYMBOL_ASK);if(volume<=0||(type==POSITION_TYPE_BUY&&stop>=bid)||(type==POSITION_TYPE_SELL&&stop<=ask)){Log("EVENT=ENTRY_REJECTED REASON=INVALID_VOLUME_OR_STOP");return;}bool ok=type==POSITION_TYPE_BUY?trade.Buy(volume,_Symbol,0,stop,0,InpTradeComment):trade.Sell(volume,_Symbol,0,stop,0,InpTradeComment);PrintTradeResult("OPEN",ok&&TradeOk(trade.ResultRetcode()));if(ok&&TradeOk(trade.ResultRetcode())){tradesToday++;RecoverPosition();managed.risk=MathAbs(managed.entry-stop);managed.stop=stop;Log("EVENT=ENTRY_ACCEPTED");}}

void OnTick()
{
 datetime server=iTime(_Symbol,PERIOD_M1,0);if(server==0||server==lastBar)return;lastBar=server;datetime ny=ServerToNewYork(iTime(_Symbol,PERIOD_M1,1));int key=DateKey(ny);if(key!=dayKey){dayKey=key;dayBaseline=AccountInfoDouble(ACCOUNT_EQUITY);tradesToday=0;riskLock=false;}RecoverPosition();ManagePosition();int minute=MinutesOf(ny);if(minute<570||minute>=960){if(InpForceFlat)CloseOwn();return;}if(!InpStrategyEnabled){if(InpDiagnostics&&((minute-570)%InpSignalIntervalMinutes==0))Log("EVENT=NO_TRADE REASON=STRATEGY_DISABLED");return;}if(InpEnableDailyLossLimit&&AccountInfoDouble(ACCOUNT_EQUITY)<=dayBaseline*(1-InpDailyLossLimitPercent/100.0))riskLock=true;if(riskLock){if(InpDiagnostics&&((minute-570)%InpSignalIntervalMinutes==0))Log("EVENT=NO_TRADE REASON=DAILY_RISK_LOCK");return;}if(tradesToday>=InpMaximumTradesPerDay){if(InpDiagnostics)Log("EVENT=NO_TRADE REASON=MAX_TRADES");return;}long spread=(long)SymbolInfoInteger(_Symbol,SYMBOL_SPREAD);if(spread>InpMaximumSpreadPoints){if(InpDiagnostics&&((minute-570)%InpSignalIntervalMinutes==0))PrintFormat("EVENT=NO_TRADE REASON=SPREAD CURRENT=%d MAX=%d",spread,InpMaximumSpreadPoints);return;}int rel=minute-570;if(rel<=0||rel%InpSignalIntervalMinutes!=0)return;NoiseBand band=BuildNoise(ny);if(!band.valid){if(InpDiagnostics)PrintFormat("EVENT=NO_TRADE REASON=WARMUP_OR_MISSING_HISTORY SESSIONS=%d REQUIRED=%d",CountCompletedSessions(ny),InpNoiseLookback);return;}double price=iClose(_Symbol,PERIOD_M1,1),kama=InpKamaMode==KAMA_ENTRY_FILTER?KAMAValue():0;bool isLong=price>band.upper+InpMinimumBreakoutDistance&&(InpKamaMode==KAMA_OFF||price>kama),isShort=price<band.lower-InpMinimumBreakoutDistance&&(InpKamaMode==KAMA_OFF||price<kama);if(!isLong&&!isShort){if(InpDiagnostics)PrintFormat("EVENT=NO_SIGNAL PRICE=%f UPPER=%f LOWER=%f SIGMA=%f",price,band.upper,band.lower,band.sigma);return;}if(ny==lastSignal)return;lastSignal=ny;OpenSignal(isLong?POSITION_TYPE_BUY:POSITION_TYPE_SELL,price,band,ny);
}
