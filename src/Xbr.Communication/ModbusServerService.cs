using System.Globalization;
using System.Text;
using DynamicExpresso;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;

namespace Xbr.Communication;

public sealed record ModbusServerOptions(
    int Port=1502,
    int SecondaryPort=1503,
    byte Station=1,
    int DataFormat=2,
    string Transport="TCP",
    string AddressMapping="No Address Mapping",
    bool RemoteWrite=true,
    bool StringReverse=false,
    bool RtuOverTcp=false,
    bool StationIsolation=false,
    bool WriteMaskCode=false,
    bool Ipv6=false)
{
    public void Validate(){
        if(Port is < 1 or > 65535||SecondaryPort is < 1 or > 65535)throw new ArgumentException("端口须在 1–65535。");
        if(DataFormat is < 0 or > 3)throw new ArgumentException("字节序索引无效。");
        if(Transport is not ("TCP" or "UDP" or "TCP+UDP"))throw new ArgumentException("传输方式无效。");
        if(!ModbusAddressMappings.Names.Contains(AddressMapping))throw new ArgumentException("未知地址映射。");
    }
}

public sealed record ModbusSimulationDefinition(
    string Name,string Address,int TimeMilliseconds,string Expression,string Encoding="");
public sealed record ModbusSimulationSnapshot(int Index,string CurrentValue,string Error="");

public sealed class ModbusServerService
{
    private readonly SemaphoreSlim gate=new(1,1);
    private ModbusTcpServer? server;
    private CancellationTokenSource? simulationCancellation;
    private Task? simulationTask;
    public bool IsRunning{get;private set;}
    public bool IsSimulating{get;private set;}
    public int Port{get;private set;}
    public event Action<string>? Message;
    public event Action<bool>? SimulationStateChanged;

    public Task StartAsync(ModbusServerOptions options)=>gate.LockAsync(()=>Task.Run(()=>{
        options.Validate();
        if(server is not null)throw new InvalidOperationException("服务器已启动。");
        var next=new ModbusTcpServer{
            ActiveTimeSpan=TimeSpan.FromHours(1),
            EnableIPv6=options.Ipv6,
            EnableWrite=options.RemoteWrite,
            Station=options.Station,
            StationDataIsolation=options.StationIsolation,
            UseModbusRtuOverTcp=options.RtuOverTcp,
            EnableWriteMaskCode=options.WriteMaskCode,
            IsStringReverse=options.StringReverse,
            DataFormat=(DataFormat)options.DataFormat
        };
        next.RegisteredAddressMapping(ModbusAddressMappings.Resolve(options.AddressMapping));
        next.OnDataReceived+=(_,_,frame)=>Message?.Invoke("客户端报文 "+Convert.ToHexString(frame));
        try{
            if(options.Transport=="TCP")next.ServerStart(options.Port,true);
            else if(options.Transport=="UDP")next.ServerStart(options.Port,false);
            else next.ServerStart(options.Port,options.SecondaryPort);
        }catch{next.ServerClose();throw;}
        server=next;Port=options.Port;IsRunning=true;
        Message?.Invoke($"Modbus {options.Transport} 服务端已启动，端口 {options.Port}");
    }));

    public async Task StopAsync(){
        await gate.LockAsync(async()=>{
            await StopSimulationCoreAsync();
            if(server is null)return;
            server.ServerClose();server=null;IsRunning=false;Message?.Invoke("Modbus 服务端已停止");
        });
    }

    public async Task StartSimulationAsync(IReadOnlyList<ModbusSimulationDefinition> definitions,Action<ModbusSimulationSnapshot> update){
        if(definitions.Count==0)throw new ArgumentException("模拟表为空。");
        if(definitions.Any(row=>row.TimeMilliseconds<0||string.IsNullOrWhiteSpace(row.Address)))
            throw new ArgumentException("模拟地址不能为空，周期不能为负数。");
        await gate.LockAsync(async()=>{
            await StopSimulationCoreAsync();
            if(server is null)throw new InvalidOperationException("请先启动服务端。");
            var scripts=definitions.Select((definition,index)=>{
                var interpreter=new Interpreter();
                interpreter.Eval(definition.Expression,new Parameter("x",0L),new Parameter("r",HslHelper.HslRandom));
                return new SimulationState(definition,interpreter,index);
            }).ToArray();
            simulationCancellation=new();
            IsSimulating=true;SimulationStateChanged?.Invoke(true);
            var token=simulationCancellation.Token;
            simulationTask=Task.Run(async()=>{
                try{
                    while(!token.IsCancellationRequested){
                        var now=DateTime.Now;
                        foreach(var state in scripts){
                            token.ThrowIfCancellationRequested();
                            if((now-state.Last).TotalMilliseconds<state.Definition.TimeMilliseconds)continue;
                            try{
                                var value=state.Interpreter.Eval(state.Definition.Expression,
                                    new Parameter("x",state.X),new Parameter("r",HslHelper.HslRandom));
                                if(value is null)continue;
                                WriteExpressionValue(server!,state.Definition,value);
                                state.Last=now;state.X++;
                                update(new(state.Index,FormatValue(value)));
                            }
                            catch(Exception ex){update(new(state.Index,"",ex.Message));return;}
                        }
                        await Task.Delay(20,token);
                    }
                }catch(OperationCanceledException){}
                finally{IsSimulating=false;SimulationStateChanged?.Invoke(false);}
            },token);
            Message?.Invoke($"模拟写入已启动，共 {definitions.Count} 项");
        });
    }

    public async Task RunSimulationOnceAsync(IReadOnlyList<ModbusSimulationDefinition> definitions,Action<ModbusSimulationSnapshot> update){
        await gate.LockAsync(()=>{
            var current=server??throw new InvalidOperationException("请先启动服务端。");
            if(IsSimulating)throw new InvalidOperationException("请先停止循环模拟。");
            for(var index=0;index<definitions.Count;index++){
                var definition=definitions[index];
                try{
                    var value=new Interpreter().Eval(definition.Expression,
                        new Parameter("x",0L),new Parameter("r",HslHelper.HslRandom));
                    WriteExpressionValue(current,definition,value);
                    update(new(index,FormatValue(value)));
                }catch(Exception ex){update(new(index,"",ex.Message));break;}
            }
            return Task.CompletedTask;
        });
    }

    public Task StopSimulationAsync()=>gate.LockAsync(StopSimulationCoreAsync);
    private async Task StopSimulationCoreAsync(){
        simulationCancellation?.Cancel();
        if(simulationTask is { } task){
            try{await task;}catch(OperationCanceledException){}
        }
        StopSimulationCore();Message?.Invoke("模拟写入已停止");
    }

    private void StopSimulationCore(){
        simulationCancellation?.Cancel();simulationCancellation?.Dispose();
        simulationCancellation=null;simulationTask=null;IsSimulating=false;
    }

    private static void WriteExpressionValue(ModbusTcpServer server,ModbusSimulationDefinition row,object? value){
        OperateResult result=value switch{
            bool v=>server.Write(row.Address,v),
            bool[] v=>server.Write(row.Address,v),
            short v=>server.Write(row.Address,v),
            short[] v=>server.Write(row.Address,v),
            ushort v=>server.Write(row.Address,v),
            ushort[] v=>server.Write(row.Address,v),
            byte v=>server.Write(row.Address,new[]{v}),
            byte[] v=>server.Write(row.Address,v),
            int v=>server.Write(row.Address,v),
            int[] v=>server.Write(row.Address,v),
            uint v=>server.Write(row.Address,v),
            uint[] v=>server.Write(row.Address,v),
            long v=>server.Write(row.Address,v),
            long[] v=>server.Write(row.Address,v),
            ulong v=>server.Write(row.Address,v),
            ulong[] v=>server.Write(row.Address,v),
            float v=>server.Write(row.Address,v),
            float[] v=>server.Write(row.Address,v),
            double v=>server.Write(row.Address,v),
            double[] v=>server.Write(row.Address,v),
            string v=>string.IsNullOrWhiteSpace(row.Encoding)?server.Write(row.Address,v):server.Write(row.Address,v,ModbusService.GetEncoding(row.Encoding)),
            null=>OperateResult.CreateSuccessResult(),
            _=>throw new NotSupportedException($"模拟表达式返回类型 {value.GetType().Name} 不受支持。")
        };
        if(!result.IsSuccess)throw new HslOperationException(result.ErrorCode,result.Message);
    }

    private static string FormatValue(object? value){
        if(value is null)return "";
        if(value is Array array)return "["+string.Join(", ",array.Cast<object>().Select(item=>Convert.ToString(item,CultureInfo.InvariantCulture)))+"]";
        return Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    }
    private sealed class SimulationState(ModbusSimulationDefinition definition,Interpreter interpreter,int index)
    {
        public ModbusSimulationDefinition Definition{get;}=definition;
        public Interpreter Interpreter{get;}=interpreter;
        public int Index{get;}=index;
        public DateTime Last{get;set;}=DateTime.Now;
        public long X{get;set;}
    }
}

internal static class SemaphoreExtensions
{
    public static async Task<T> LockAsync<T>(this SemaphoreSlim gate,Func<Task<T>> action){
        await gate.WaitAsync();
        try{return await action();}finally{gate.Release();}
    }
    public static async Task LockAsync(this SemaphoreSlim gate,Func<Task> action){
        await gate.WaitAsync();
        try{await action();}finally{gate.Release();}
    }
}
