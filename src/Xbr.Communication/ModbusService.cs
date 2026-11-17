using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using System.Globalization;
using System.Text;

namespace Xbr.Communication;

// Serializes all operations, including disconnect, on a single connection.
public sealed partial class ModbusService
{
    private readonly SemaphoreSlim gate = new(1);
    private ModbusTcpNet? client;
    public async Task ConnectAsync(ConnectionOptions options) => await Execute(() => {
        options.Validate();
        client?.ConnectClose(); client = null;
        var next = new ModbusTcpNet(options.Host, options.Port, options.Station) {
            ConnectTimeOut=options.ConnectTimeout, ReceiveTimeOut=options.ReceiveTimeout,
            AddressStartWithZero=options.AddressStartWithZero, DataFormat=(DataFormat)options.DataFormat,
            IsStringReverse=options.StringReverse, IsCheckMessageId=options.CheckMessageId,
            StationCheckMatch=options.StationCheck, DisableFunctionCode06=options.DisableFunctionCode06,
            BroadcastStation=options.BroadcastStation, WordReadBatchLength=options.WordReadBatchLength
        };
        next.RegisteredAddressMapping(ModbusAddressMappings.Resolve(options.AddressMapping));
        var result=next.ConnectServer();
        if(!result.IsSuccess) { next.ConnectClose(); Check(result); }
        client=next; return true;
    });
    public async Task DisconnectAsync() => await Execute(() => { client?.ConnectClose(); client=null; return true; });
    public Task<string> ReadAsync(string address, string type, ushort count) => Execute(() => {
        var c=client ?? throw new InvalidOperationException("请先连接设备。");
        if(count==0) throw new ArgumentException("长度必须大于零。");
        return type switch {
            "Bool"=>Format(c.ReadBool(address,count)), "Int16"=>Format(c.ReadInt16(address,count)),
            "UInt16"=>Format(c.ReadUInt16(address,count)), "Int32"=>Format(c.ReadInt32(address,count)),
            "UInt32"=>Format(c.ReadUInt32(address,count)), "Int64"=>Format(c.ReadInt64(address,count)),
            "UInt64"=>Format(c.ReadUInt64(address,count)), "Float"=>Format(c.ReadFloat(address,count)),
            "Double"=>Format(c.ReadDouble(address,count)), "String"=>Format(c.ReadString(address,count,Encoding.UTF8)),
            "Hex"=>Convert.ToHexString(Value(c.Read(address,count))), _=>throw new NotSupportedException(type)
        };
    });
    public Task<string> WriteAsync(string address, string type, string text) => Execute(() => {
        var c=client ?? throw new InvalidOperationException("请先连接设备。");
        var parts=text.Trim().Trim('[',']').Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        T[] Parse<T>(Func<string,T> parse)=>parts.Select(parse).ToArray();
        var culture=CultureInfo.InvariantCulture;
        bool array=text.Trim().StartsWith("[") || text.Contains(",");
        OperateResult r=type switch {
            "Bool"=>array ? c.Write(address,Parse(x=>x=="1" || (x!="0" && bool.Parse(x)))) : c.Write(address,text=="1" || (text!="0" && bool.Parse(text))),
            "Int16"=>array ? c.Write(address,Parse(x=>short.Parse(x,culture))) : c.Write(address,short.Parse(text,culture)), "UInt16"=>array ? c.Write(address,Parse(x=>ushort.Parse(x,culture))) : c.Write(address,ushort.Parse(text,culture)),
            "Int32"=>array ? c.Write(address,Parse(x=>int.Parse(x,culture))) : c.Write(address,int.Parse(text,culture)), "UInt32"=>array ? c.Write(address,Parse(x=>uint.Parse(x,culture))) : c.Write(address,uint.Parse(text,culture)),
            "Int64"=>array ? c.Write(address,Parse(x=>long.Parse(x,culture))) : c.Write(address,long.Parse(text,culture)), "UInt64"=>array ? c.Write(address,Parse(x=>ulong.Parse(x,culture))) : c.Write(address,ulong.Parse(text,culture)),
            "Float"=>array ? c.Write(address,Parse(x=>float.Parse(x,culture))) : c.Write(address,float.Parse(text,culture)), "Double"=>array ? c.Write(address,Parse(x=>double.Parse(x,culture))) : c.Write(address,double.Parse(text,culture)),
            "String"=>c.Write(address,text,Encoding.UTF8), "Hex"=>c.Write(address,Convert.FromHexString(text.Replace(" ",""))),
            _=>throw new NotSupportedException(type)
        }; Check(r); return "写入成功";
    });
    public Task<string> RawAsync(string hex, bool core) => Execute(() => {
        var c=client ?? throw new InvalidOperationException("请先连接设备。");
        return Convert.ToHexString(Value(c.ReadFromCoreServer(Convert.FromHexString(hex.Replace(" ","")),true,core)));
    });
    public Task<string> ReadWriteAsync(string readAddress, ushort length, string writeAddress, string hex) => Execute(() => {
        var c=client ?? throw new InvalidOperationException("请先连接设备。");
        return Convert.ToHexString(Value(c.ReadWrite(readAddress,length,writeAddress,Convert.FromHexString(hex.Replace(" ","")))));
    });

    // Holds the connection lock for the scan; always restores mutable connection settings.
    public async Task SearchStationsAsync(int start, int end, int timeout, string address, ushort count,
        bool word, bool stopOnMatch, IProgress<StationSearchResult>? progress, CancellationToken cancellationToken)
    {
        if(start<0 || end>255 || start>end || timeout<=0 || count==0)
            throw new ArgumentException("站号范围须为 0–255、起始不大于结束；超时和长度须大于零。");
        await gate.WaitAsync(cancellationToken);
        try {
            var c=client ?? throw new InvalidOperationException("请先连接设备。");
            await Task.Run(async () => {
                var oldStation=c.Station; var oldTimeout=c.ReceiveTimeOut;
                var pipe=c.CommunicationPipe as HslCommunication.Core.Pipe.PipeTcpNet;
                var oldClose=pipe?.CloseOnRecvTimeOutTick;
                try {
                    c.ReceiveTimeOut=timeout;
                    if(pipe!=null)pipe.CloseOnRecvTimeOutTick=10000;
                    for(int station=start;station<=end;station++) {
                        cancellationToken.ThrowIfCancellationRequested();
                        c.Station=(byte)station;
                        bool success; string content; int code;
                        if(word) {var r=c.Read(address,count);success=r.IsSuccess;code=r.ErrorCode;content=r.IsSuccess?Convert.ToHexString(r.Content):r.Message;}
                        else {var r=c.ReadBool(address,count);success=r.IsSuccess;code=r.ErrorCode;content=r.IsSuccess?string.Join(", ",r.Content):r.Message;}
                        progress?.Report(new(station,success,content,code));
                        if(success) {
                            await Task.Delay(200,cancellationToken);
                            if(stopOnMatch)break;
                        }
                    }
                } finally {
                    c.Station=oldStation;c.ReceiveTimeOut=oldTimeout;
                    if(pipe!=null && oldClose.HasValue)pipe.CloseOnRecvTimeOutTick=oldClose.Value;
                }
            },cancellationToken);
        } finally {gate.Release();}
    }
    private async Task<T> Execute<T>(Func<T> action) {
        await gate.WaitAsync(); try { return await Task.Run(action); } finally { gate.Release(); }
    }
    private static void Check(OperateResult r) { if(!r.IsSuccess) throw new HslOperationException(r.ErrorCode,r.Message); }
    private static T Value<T>(OperateResult<T> r) { Check(r); return r.Content; }
    private static string Format<T>(OperateResult<T> r) {
        var value=Value(r);
        return value is Array a ? string.Join(", ",a.Cast<object>().Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture))) : Convert.ToString(value,CultureInfo.InvariantCulture) ?? "";
    }
}

public sealed record StationSearchResult(int Station,bool Success,string Content,int ErrorCode);
public sealed class HslOperationException(int errorCode,string message) : IOException($"Error {errorCode}: {message}")
{
    public int ErrorCode {get;}=errorCode;
    public string HslMessage {get;}=message;
}
