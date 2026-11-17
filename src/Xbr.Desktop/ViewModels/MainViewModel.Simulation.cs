using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbr.Communication;
using Xbr.Desktop.Models;
using System.Xml.Linq;

namespace Xbr.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly ModbusServerService simulationServer=new();
    public ObservableCollection<SimulationRow> SimulationRows{get;}=new();
    public string[] ServerTransports{get;}=["TCP","UDP","TCP+UDP"];
    public string[] ServerMappings{get;}=ModbusAddressMappings.Names;
    [ObservableProperty] private string serverPort="1502";
    [ObservableProperty] private string serverSecondaryPort="1503";
    [ObservableProperty] private string serverStation="1";
    [ObservableProperty] private int serverFormatIndex=2;
    [ObservableProperty] private int serverTransportIndex;
    [ObservableProperty] private int serverMappingIndex;
    [ObservableProperty] private bool serverRemoteWrite=true;
    [ObservableProperty] private bool serverStringReverse;
    [ObservableProperty] private bool serverRtuOverTcp;
    [ObservableProperty] private bool serverStationIsolation;
    [ObservableProperty] private bool serverWriteMaskCode;
    [ObservableProperty] private bool serverIpv6;
    [ObservableProperty] private bool isServerRunning;
    [ObservableProperty] private bool isSimulating;
    [ObservableProperty] private string serverStatus="未启动";
    [ObservableProperty] private SimulationRow? selectedSimulationRow;
    [ObservableProperty] private string simulationLog="";
    [ObservableProperty] private string simulationPath="simulation-table.xml";

    public MainViewModel(){
        simulationServer.Message+=message=>Dispatcher.UIThread.Post(()=>AppendSimulationLog(message));
        simulationServer.SimulationStateChanged+=running=>Dispatcher.UIThread.Post(()=>IsSimulating=running);
        SimulationRows.Add(new SimulationRow{Name="计数器",Address="100",TimeMilliseconds="100",Expression="x * 10",Mark="递增"});
        SimulationRows.Add(new SimulationRow{Name="随机数",Address="104",TimeMilliseconds="300",Expression="r.Next(0, 1000)",Mark="表达式 r"});
        InitializeProtocolCatalog();
        InitializeIndustrial();
    }

    [RelayCommand] private async Task StartServer(){
        try{
            if(!int.TryParse(ServerPort,out var port)||!int.TryParse(ServerSecondaryPort,out var secondary)||!byte.TryParse(ServerStation,out var station))
                throw new ArgumentException("端口或站号格式无效。");
            await simulationServer.StartAsync(new(
                Port:port,SecondaryPort:secondary,Station:station,DataFormat:ServerFormatIndex,
                Transport:ServerTransports[ServerTransportIndex],AddressMapping:ServerMappings[ServerMappingIndex],
                RemoteWrite:ServerRemoteWrite,StringReverse:ServerStringReverse,RtuOverTcp:ServerRtuOverTcp,
                StationIsolation:ServerStationIsolation,WriteMaskCode:ServerWriteMaskCode,Ipv6:ServerIpv6));
            IsServerRunning=true;ServerStatus=$"运行中 · {ServerTransports[ServerTransportIndex]} :{port}";
        }catch(Exception ex){ServerStatus=ex.Message;AppendSimulationLog(ex.Message);}
    }
    [RelayCommand] private async Task StopServer(){
        await simulationServer.StopAsync();IsServerRunning=false;IsSimulating=false;ServerStatus="已停止";
    }
    [RelayCommand] private void UseSimulatorAsClient(){
        if(ServerTransportIndex==1||ServerRtuOverTcp){AppendSimulationLog("当前客户端页使用 Modbus TCP；请将模拟器设为 TCP 或 TCP+UDP，并关闭 RTU over TCP。");return;}
        if(IsConnected){AppendSimulationLog("请先断开当前客户端，再填入本机参数。");return;}
        if(!int.TryParse(ServerPort,out var port))return;
        Host="127.0.0.1";Port=port.ToString();Station=ServerStation;FormatIndex=ServerFormatIndex;
        StringReverse=ServerStringReverse;
        AddressMappingIndex=Array.IndexOf(AddressMappings,ServerMappings[ServerMappingIndex]);
        Address=ServerMappings[ServerMappingIndex] switch{"No Address Mapping"=>"100","Inovance AM" or "Inovance EVO"=>"MW100",_=>"D100"};
        BatchAddress=Address;SearchAddress=Address;
        Status=$"已填入本机模拟器 {Host}:{Port}";AppendSimulationLog(Status);
    }
    [RelayCommand] private void AddSimulationRow(){
        if(IsSimulating)return;
        var row=new SimulationRow{Name=$"点位{SimulationRows.Count+1}",Address=(100+SimulationRows.Count).ToString()};
        SimulationRows.Add(row);SelectedSimulationRow=row;
    }
    [RelayCommand] private void RemoveSimulationRow(){if(!IsSimulating&&SelectedSimulationRow is not null)SimulationRows.Remove(SelectedSimulationRow);}
    [RelayCommand] private void ClearSimulationRows(){if(!IsSimulating)SimulationRows.Clear();}
    [RelayCommand] private void SaveSimulationTable(){
        try{
            var path=Path.GetFullPath(SimulationPath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(File.Exists(path))File.Copy(path,path+"."+DateTime.Now.ToString("yyyyMMddHHmmssfff")+".bak");
            new XElement("SimulationTable",SimulationRows.Select(row=>row.ToXml())).Save(path);
            AppendSimulationLog("模拟表已保存（官方 SimulateWrite 属性结构）");
        }catch(Exception ex){AppendSimulationLog(ex.Message);}
    }
    [RelayCommand] private void LoadSimulationTable(){
        if(IsSimulating)return;
        try{
            var root=XElement.Load(SimulationPath);
            var rows=root.DescendantsAndSelf("SimulateWrite").Select(SimulationRow.FromXml).ToArray();
            foreach(var row in rows)row.Definition();
            SimulationRows.Clear();foreach(var row in rows)SimulationRows.Add(row);
            AppendSimulationLog($"已加载 {rows.Length} 项模拟表达式");
        }catch(Exception ex){AppendSimulationLog(ex.Message);}
    }
    [RelayCommand] private async Task StartSimulation(){
        try{
            var definitions=SimulationRows.Select(row=>row.Definition()).ToArray();
            await simulationServer.StartSimulationAsync(definitions,UpdateSimulationRow);
            IsSimulating=true;
        }catch(Exception ex){AppendSimulationLog("模拟启动失败："+ex.Message);}
    }
    [RelayCommand] private async Task StopSimulation(){
        await simulationServer.StopSimulationAsync();IsSimulating=false;
    }
    [RelayCommand] private async Task RunSimulationOnce(){
        try{
            var definitions=SimulationRows.Select(row=>row.Definition()).ToArray();
            await simulationServer.RunSimulationOnceAsync(definitions,UpdateSimulationRow);
        }catch(Exception ex){AppendSimulationLog("单次模拟失败："+ex.Message);}
    }
    private void UpdateSimulationRow(ModbusSimulationSnapshot snapshot){
        Dispatcher.UIThread.Post(()=>{
            if(snapshot.Index<0||snapshot.Index>=SimulationRows.Count)return;
            var row=SimulationRows[snapshot.Index];row.CurrentValue=snapshot.CurrentValue;
            row.Error=snapshot.Error;if(!string.IsNullOrEmpty(snapshot.Error))AppendSimulationLog($"{row.Address}: {snapshot.Error}");
        });
    }
    private void AppendSimulationLog(string message){
        SimulationLog=$"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}{SimulationLog}";
        if(SimulationLog.Length>12000)SimulationLog=SimulationLog[..12000];
    }
    private async Task StopSimulatorAsync(){
        await simulationServer.StopAsync();IsSimulating=false;IsServerRunning=false;ServerStatus="已停止";
    }
}
