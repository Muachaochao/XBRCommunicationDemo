using System.Collections.ObjectModel;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbr.Communication;
namespace Xbr.Desktop.ViewModels;
public partial class MainViewModel : ViewModelBase
{
    private readonly ModbusService service=new();
    private CancellationTokenSource? polling;
    private Task? pollingTask;
    private XElement? imported;
    public string[] Formats {get;}=["ABCD","BADC","CDAB","DCBA"];
    public string[] Types {get;}=["Bool","Int16","UInt16","Int32","UInt32","Int64","UInt64","Float","Double","String","Hex"];
    public ObservableCollection<string> Logs {get;}=new();
    [ObservableProperty] private string host="127.0.0.1";
    [ObservableProperty] private string port="502";
    [ObservableProperty] private string station="1";
    [ObservableProperty] private string connectTimeout="5000";
    [ObservableProperty] private string receiveTimeout="10000";
    [ObservableProperty] private int formatIndex=2;
    [ObservableProperty] private bool addressZero=true;
    [ObservableProperty] private bool stringReverse;
    [ObservableProperty] private bool messageCheck=true;
    [ObservableProperty] private bool stationCheck=true;
    [ObservableProperty] private bool disable06;
    [ObservableProperty] private string broadcast="";
    [ObservableProperty] private string batchLength="";
    [ObservableProperty] private string address="100";
    [ObservableProperty] private string dataType="UInt16";
    [ObservableProperty] private string length="1";
    [ObservableProperty] private string writeValue="0";
    [ObservableProperty] private string result="等待读取";
    [ObservableProperty] private string rawHex="01 03 00 00 00 02";
    [ObservableProperty] private bool coreMessage=true;
    [ObservableProperty] private string interval="500";
    [ObservableProperty] private string configPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XbrCommunicationDemo","modbus.xml");
    [ObservableProperty] private string status="未连接";
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isPolling;
    [ObservableProperty] private bool isBusy;
    public bool CanEditConnection=>!IsConnected&&!IsBusy;
    public bool CanOperate=>IsConnected&&!IsBusy&&!IsPolling&&!IsSearching;
    public bool CanDisconnect=>IsConnected&&!IsBusy;
    partial void OnIsConnectedChanged(bool value)=>Refresh();
    partial void OnIsBusyChanged(bool value)=>Refresh();
    partial void OnIsPollingChanged(bool value)=>Refresh();
    private void Refresh(){OnPropertyChanged(nameof(CanEditConnection));OnPropertyChanged(nameof(CanOperate));OnPropertyChanged(nameof(CanDisconnect));}
    private void Log(string value){Logs.Insert(0,$"{DateTime.Now:HH:mm:ss.fff}  {value}");while(Logs.Count>500)Logs.RemoveAt(Logs.Count-1);}
    private ConnectionOptions Options()=>new(){Host=Host,Port=int.Parse(Port),Station=byte.Parse(Station),ConnectTimeout=int.Parse(ConnectTimeout),ReceiveTimeout=int.Parse(ReceiveTimeout),DataFormat=FormatIndex,AddressStartWithZero=AddressZero,StringReverse=StringReverse,CheckMessageId=MessageCheck,StationCheck=StationCheck,DisableFunctionCode06=Disable06,BroadcastStation=string.IsNullOrWhiteSpace(Broadcast)?-1:int.Parse(Broadcast),WordReadBatchLength=string.IsNullOrWhiteSpace(BatchLength)?120:int.Parse(BatchLength)};
    private async Task Run(Func<Task> action){if(IsBusy)return;IsBusy=true;try{await action();}catch(Exception ex){Result=ex.Message;Log(ex.Message);}finally{IsBusy=false;}}
    [RelayCommand] private Task Connect()=>Run(async()=>{await service.ConnectAsync(Options());IsConnected=true;Status=$"已连接 · {Host}:{Port}";Log(Status);});
    [RelayCommand] private Task Disconnect()=>Run(async()=>{await StopPollingAsync();await service.DisconnectAsync();IsConnected=false;Status="未连接";Log("连接已关闭");});
    [RelayCommand] private Task Read()=>Run(async()=>{Result=await service.ReadAsync(Address,DataType,ushort.Parse(Length));Log($"读 {Address} [{DataType}] → {Result}");});
    [RelayCommand] private Task Write()=>Run(async()=>{Result=await service.WriteAsync(Address,DataType,WriteValue);Log($"写 {Address} [{DataType}] → {Result}");});
    [RelayCommand] private Task SendRaw()=>Run(async()=>{Result=await service.RawAsync(RawHex,CoreMessage);Log($"报文响应 → {Result}");});
    [RelayCommand] private void StartPolling(){
        if(!CanOperate)return;
        if(!int.TryParse(Interval,out var delay)||delay<50){Log("轮询间隔至少 50ms。");return;}
        if(!ushort.TryParse(Length,out var count)||count==0){Log("长度必须为正整数。");return;}
        polling=new();IsPolling=true;pollingTask=PollAsync(Address,DataType,count,delay,polling.Token);
    }
    private async Task PollAsync(string address,string type,ushort count,int delay,CancellationToken token){
        try{while(!token.IsCancellationRequested){Result=await service.ReadAsync(address,type,count);Log($"轮询 {address} → {Result}");await Task.Delay(delay,token);}}
        catch(OperationCanceledException){}catch(Exception ex){Log("轮询停止："+ex.Message);}finally{IsPolling=false;}
    }
    [RelayCommand] private async Task StopPolling()=>await StopPollingAsync();
    private async Task StopPollingAsync(){polling?.Cancel();if(pollingTask!=null)await pollingTask;polling?.Dispose();polling=null;pollingTask=null;IsPolling=false;}
    [RelayCommand] private void SaveConfig(){try{var o=Options();o.Validate();var file=Path.GetFullPath(ConfigPath);Directory.CreateDirectory(Path.GetDirectoryName(file)!);if(File.Exists(file))File.Copy(file,file+"."+DateTime.Now.ToString("yyyyMMddHHmmssfff")+".bak");ConfigurationStore.Save(o,imported).Save(file);Log("配置已保存；消息号校验按原版不写入 XML。");}catch(Exception ex){Log(ex.Message);}}
    [RelayCommand] private void LoadConfig(){if(!CanEditConnection)return;try{var e=XElement.Load(ConfigPath);var o=ConfigurationStore.Load(e);imported=e;Host=o.Host;Port=o.Port.ToString();Station=o.Station.ToString();ConnectTimeout=o.ConnectTimeout.ToString();ReceiveTimeout=o.ReceiveTimeout.ToString();FormatIndex=o.DataFormat;AddressZero=o.AddressStartWithZero;StringReverse=o.StringReverse;StationCheck=o.StationCheck;Disable06=o.DisableFunctionCode06;Broadcast=o.BroadcastStation==-1?"":o.BroadcastStation.ToString();BatchLength=o.WordReadBatchLength.ToString();Log("单设备配置已加载");}catch(Exception ex){Log(ex.Message);}}
    public async Task CloseAsync(){await StopSearch();await StopPollingAsync();await service.DisconnectAsync();}
}
