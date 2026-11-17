using System.Globalization;
using System.Text;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Omron;
using HslCommunication.Profinet.Siemens;

namespace Xbr.Communication;

// The same HSL client/server classes used by FormMelsecBinary, FormSiemens and FormOmron.
public sealed class IndustrialPlcService
{
    public static readonly string[] Protocols=["Mitsubishi MC 3E Binary", "Siemens S7-1200", "Omron FINS TCP"];
    private readonly SemaphoreSlim gate=new(1,1);
    private DeviceTcpNet? client;
    private DeviceServer? server;
    public bool IsConnected=>client is not null;
    public bool IsServerRunning=>server is not null;
    public event Action<string>? Message;
    public static string DefaultAddress(int protocol)=>protocol==1?"M100":"D100";

    public Task ConnectAsync(int protocol,string host,int port)=>Run(()=>{
        Validate(protocol,port);
        if(string.IsNullOrWhiteSpace(host))throw new ArgumentException("主机不能为空。");
        client?.ConnectClose();client=null;
        DeviceTcpNet next=protocol switch{
            0=>new MelsecMcNet(),
            1=>new SiemensS7Net(SiemensPLCS.S1200),
            2=>new OmronFinsNet(),
            _=>throw new ArgumentOutOfRangeException(nameof(protocol))
        };
        next.IpAddress=host;next.Port=port;next.ConnectTimeOut=2000;next.ReceiveTimeOut=2000;
        var result=next.ConnectServer();
        if(!result.IsSuccess){next.ConnectClose();DeviceValues.Check(result);}
        client=next;Message?.Invoke($"已连接 {Protocols[protocol]} {host}:{port}");
    });
    public Task DisconnectAsync()=>Run(()=>{client?.ConnectClose();client=null;});
    public Task StartServerAsync(int protocol,int port)=>Run(()=>{
        Validate(protocol,port);
        if(server is not null)throw new InvalidOperationException("请先停止当前服务端。");
        DeviceServer next=protocol switch{
            0=>new MelsecMcServer(true),1=>new SiemensS7Server(),2=>new OmronFinsServer(),
            _=>throw new ArgumentOutOfRangeException(nameof(protocol))
        };
        next.ActiveTimeSpan=TimeSpan.FromHours(1);
        next.OnDataReceived+=(_,_,frame)=>Message?.Invoke("RX "+Convert.ToHexString(frame));
        try{next.ServerStart(port);server=next;}
        catch{next.ServerClose();throw;}
        Message?.Invoke($"{Protocols[protocol]} 模拟服务端 :{port}");
    });
    public Task StopServerAsync()=>Run(()=>{server?.ServerClose();server=null;});
    public Task<string> ReadAsync(string address,string type,ushort count,bool local=false)=>Run(()=>DeviceValues.Read(Device(local),address,type,count));
    public Task WriteAsync(string address,string type,string text,bool local=false)=>Run(()=>DeviceValues.Write(Device(local),address,type,text));
    public Task SaveMemoryAsync(string path)=>Run(()=>(server??throw new InvalidOperationException("请先启动服务端。")).SaveDataPool(path));
    public Task LoadMemoryAsync(string path)=>Run(()=>(server??throw new InvalidOperationException("请先启动服务端。")).LoadDataPool(path));
    private IReadWriteNet Device(bool local)=>local
        ?server??throw new InvalidOperationException("请先启动服务端。")
        :client??throw new InvalidOperationException("请先连接设备。");
    private static void Validate(int protocol,int port){
        if(protocol<0||protocol>=Protocols.Length)throw new ArgumentException("未知 PLC 协议。");
        if(port is <1 or >65535)throw new ArgumentException("端口须在 1–65535。");
    }
    private Task Run(Action action)=>gate.LockAsync(()=>Task.Run(action));
    private Task<T> Run<T>(Func<T> action)=>gate.LockAsync(()=>Task.Run(action));
}

internal static class DeviceValues
{
    public static string Read(IReadWriteNet device,string address,string type,ushort count){
        if(count==0)throw new ArgumentException("长度必须大于零。");
        return type switch{
            "Bool"=>Format(device.ReadBool(address,count)),"Int16"=>Format(device.ReadInt16(address,count)),
            "UInt16"=>Format(device.ReadUInt16(address,count)),"Int32"=>Format(device.ReadInt32(address,count)),
            "UInt32"=>Format(device.ReadUInt32(address,count)),"Int64"=>Format(device.ReadInt64(address,count)),
            "UInt64"=>Format(device.ReadUInt64(address,count)),"Float"=>Format(device.ReadFloat(address,count)),
            "Double"=>Format(device.ReadDouble(address,count)),"String"=>Format(device.ReadString(address,count,Encoding.UTF8)),
            "Hex"=>Convert.ToHexString(Value(device.Read(address,count))),_=>throw new NotSupportedException(type)
        };
    }
    public static void Write(IReadWriteNet device,string address,string type,string text){
        var parts=text.Trim().Trim('[',']').Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        T[] Parse<T>(Func<string,T> parse)=>parts.Select(parse).ToArray();
        var culture=CultureInfo.InvariantCulture;
        bool array=text.Trim().StartsWith('[')||text.Contains(',');
        bool Bool(string value)=>value=="1"||(value!="0"&&bool.Parse(value));
        OperateResult result=type switch{
            "Bool"=>array?device.Write(address,Parse(Bool)):device.Write(address,Bool(text)),
            "Int16"=>array?device.Write(address,Parse(x=>short.Parse(x,culture))):device.Write(address,short.Parse(text,culture)),
            "UInt16"=>array?device.Write(address,Parse(x=>ushort.Parse(x,culture))):device.Write(address,ushort.Parse(text,culture)),
            "Int32"=>array?device.Write(address,Parse(x=>int.Parse(x,culture))):device.Write(address,int.Parse(text,culture)),
            "UInt32"=>array?device.Write(address,Parse(x=>uint.Parse(x,culture))):device.Write(address,uint.Parse(text,culture)),
            "Int64"=>array?device.Write(address,Parse(x=>long.Parse(x,culture))):device.Write(address,long.Parse(text,culture)),
            "UInt64"=>array?device.Write(address,Parse(x=>ulong.Parse(x,culture))):device.Write(address,ulong.Parse(text,culture)),
            "Float"=>array?device.Write(address,Parse(x=>float.Parse(x,culture))):device.Write(address,float.Parse(text,culture)),
            "Double"=>array?device.Write(address,Parse(x=>double.Parse(x,culture))):device.Write(address,double.Parse(text,culture)),
            "String"=>device.Write(address,text,Encoding.UTF8),"Hex"=>device.Write(address,Convert.FromHexString(text.Replace(" ",""))),
            _=>throw new NotSupportedException(type)
        };
        Check(result);
    }
    public static void Check(OperateResult result){if(!result.IsSuccess)throw new HslOperationException(result.ErrorCode,result.Message);}
    private static T Value<T>(OperateResult<T> result){Check(result);return result.Content;}
    private static string Format<T>(OperateResult<T> result){
        var value=Value(result);
        return value is Array array?string.Join(", ",array.Cast<object>().Select(item=>Convert.ToString(item,CultureInfo.InvariantCulture))):Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    }
}
