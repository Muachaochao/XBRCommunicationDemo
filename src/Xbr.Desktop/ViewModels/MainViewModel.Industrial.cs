using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbr.Communication;

namespace Xbr.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly IndustrialPlcService industrial=new();
    public string[] IndustrialProtocols{get;}=IndustrialPlcService.Protocols;
    [ObservableProperty] private int industrialProtocolIndex;
    [ObservableProperty] private string industrialHost="127.0.0.1";
    [ObservableProperty] private string industrialPort="1600";
    [ObservableProperty] private string industrialAddress="D100";
    [ObservableProperty] private string industrialType="Int16";
    [ObservableProperty] private string industrialCount="1";
    [ObservableProperty] private string industrialValue="123";
    [ObservableProperty] private string industrialResult="";
    [ObservableProperty] private string industrialLog="";
    [ObservableProperty] private string industrialMemoryPath="plc-memory.bin";
    [ObservableProperty] private bool industrialConnected;
    [ObservableProperty] private bool industrialServerRunning;
    [ObservableProperty] private bool industrialBusy;
    public bool CanChangeIndustrial=>!IndustrialConnected&&!IndustrialServerRunning&&!IndustrialBusy;
    partial void OnIndustrialConnectedChanged(bool value)=>OnPropertyChanged(nameof(CanChangeIndustrial));
    partial void OnIndustrialServerRunningChanged(bool value)=>OnPropertyChanged(nameof(CanChangeIndustrial));
    partial void OnIndustrialBusyChanged(bool value)=>OnPropertyChanged(nameof(CanChangeIndustrial));
    partial void OnIndustrialProtocolIndexChanged(int value)=>IndustrialAddress=IndustrialPlcService.DefaultAddress(value);
    private void InitializeIndustrial()=>industrial.Message+=message=>Dispatcher.UIThread.Post(()=>{
        IndustrialLog=$"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}{IndustrialLog}";
        if(IndustrialLog.Length>12000)IndustrialLog=IndustrialLog[..12000];
    });
    private async Task RunIndustrial(Func<Task> action){
        if(IndustrialBusy)return;
        IndustrialBusy=true;
        try{await action();}catch(Exception ex){IndustrialResult=ex.Message;}
        finally{IndustrialConnected=industrial.IsConnected;IndustrialServerRunning=industrial.IsServerRunning;IndustrialBusy=false;}
    }
    [RelayCommand] private Task ConnectIndustrial()=>RunIndustrial(()=>industrial.ConnectAsync(IndustrialProtocolIndex,IndustrialHost,int.Parse(IndustrialPort)));
    [RelayCommand] private Task DisconnectIndustrial()=>RunIndustrial(()=>industrial.DisconnectAsync());
    [RelayCommand] private Task StartIndustrialServer()=>RunIndustrial(()=>industrial.StartServerAsync(IndustrialProtocolIndex,int.Parse(IndustrialPort)));
    [RelayCommand] private Task StopIndustrialServer()=>RunIndustrial(async()=>{await industrial.DisconnectAsync();await industrial.StopServerAsync();});
    [RelayCommand] private Task ReadIndustrial()=>RunIndustrial(async()=>IndustrialResult=await industrial.ReadAsync(IndustrialAddress,IndustrialType,ushort.Parse(IndustrialCount)));
    [RelayCommand] private Task WriteIndustrial()=>RunIndustrial(async()=>{await industrial.WriteAsync(IndustrialAddress,IndustrialType,IndustrialValue);IndustrialResult="客户端写入成功";});
    [RelayCommand] private Task ReadIndustrialLocal()=>RunIndustrial(async()=>IndustrialResult=await industrial.ReadAsync(IndustrialAddress,IndustrialType,ushort.Parse(IndustrialCount),true));
    [RelayCommand] private Task WriteIndustrialLocal()=>RunIndustrial(async()=>{await industrial.WriteAsync(IndustrialAddress,IndustrialType,IndustrialValue,true);IndustrialResult="服务端内存已写入";});
    [RelayCommand] private Task SaveIndustrialMemory()=>RunIndustrial(async()=>{await industrial.SaveMemoryAsync(IndustrialMemoryPath);IndustrialResult="服务端数据池已保存";});
    [RelayCommand] private Task LoadIndustrialMemory()=>RunIndustrial(async()=>{await industrial.LoadMemoryAsync(IndustrialMemoryPath);IndustrialResult="服务端数据池已恢复";});
    private async Task CloseIndustrialAsync(){await industrial.DisconnectAsync();await industrial.StopServerAsync();}
}
