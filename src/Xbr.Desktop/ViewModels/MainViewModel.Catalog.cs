using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbr.Desktop.Models;

namespace Xbr.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly ProtocolCatalogEntry[] protocolCatalog=[
        new("PLC","Siemens","S7-200/300/400/1200/1500","TCP / ISO-on-TCP","PLC/Siemens/FormSiemens.cs","S7-1200 基础读写已接入"),
        new("PLC","Mitsubishi","MC 1E/3E/3C/4C, FX Serial","TCP / UDP / Serial","PLC/Melsec/FormMelsecBinary.cs","MC 3E Binary 基础读写已接入"),
        new("PLC","Omron","CIP / FINS / HostLink","TCP / UDP / Serial","PLC/Omron/FormOmron.cs","FINS TCP 基础读写已接入"),
        new("PLC","Allen-Bradley","CIP / PCCC / DF1","TCP / Serial","PLC/AllenBrandly/Form*.cs","后续适配"),
        new("PLC","Schneider / Modbus","Modbus TCP/RTU","TCP / Serial","DemoControl/UserControlReadWrite*","已支持"),
        new("PLC","Inovance","AM / H3U / H5U / Easy / EVO","Modbus TCP / RTU","PLC/Inovance/Form*.cs","映射已支持"),
        new("PLC","Delta","AS / DVP","Modbus TCP / RTU / ASCII","PLC/Delta/Form*.cs","映射已支持"),
        new("PLC","XinJE","XC / XD / XL / Internal","Modbus / Internal TCP","PLC/XINJE/Form*.cs","映射已支持"),
        new("PLC","WeCon","LX5V","Modbus TCP / RTU","PLC/WeCon/Form*.cs","映射已支持"),
        new("PLC","INVT","TS / Modbus","Modbus TCP / RTU","PLC/Invt/Form*.cs","映射已支持"),
        new("PLC","Beckhoff","ADS","TCP","PLC/Beckhoff/Form*.cs","后续适配"),
        new("PLC","Fatek","FBs / Program","TCP / Serial","PLC/Fatek/Form*.cs","后续适配"),
        new("PLC","Fuji","SPB / SPH / CST","TCP / UDP / Serial","PLC/Fuji/Form*.cs","后续适配"),
        new("PLC","Panasonic","Mewtocol","TCP / Serial","PLC/Panasonic/Form*.cs","后续适配"),
        new("PLC","Keyence","KV / SR2000","TCP / Serial","PLC/Keyence/Form*.cs","后续适配"),
        new("PLC","Yaskawa","Memobus","TCP / UDP","PLC/YASKAWA/Form*.cs","后续适配"),
        new("PLC","LS Electric","XGT / Cnet / FEnet","TCP / Serial","PLC/Lsis/Form*.cs","后续适配"),
        new("PLC","GE","SRTP","TCP","PLC/Ge/Form*.cs","后续适配"),
        new("PLC","Yokogawa","Link / F3","TCP / Serial","PLC/Yokogawa/Form*.cs","后续适配"),
        new("PLC","Freedom","Ethernet / UDP / Serial","TCP / UDP / Serial","PLC/Freedom/Form*.cs","后续适配"),
        new("PLC","Toyopuc","Ethernet PUC","TCP","PLC/Toyota/Form*.cs","后续适配"),
        new("PLC","Vigor","Ethernet / Serial","TCP / Serial","PLC/Vigor/Form*.cs","后续适配"),
        new("PLC","Turck","RFID Reader","TCP / Serial","PLC/Turck/Form*.cs","后续适配"),
        new("Robot","ABB","Web API / Robot Server","HTTP / TCP","Robot/FormABB*.cs","后续适配"),
        new("Robot","KUKA","Robot TCP","TCP","Robot/FormKuka*.cs","后续适配"),
        new("Robot","FANUC","Robot TCP / Server","TCP","Robot/FormFanuc*.cs","后续适配"),
        new("Robot","Yaskawa","YRC1000 / High Ethernet","TCP","Robot/FormYRC*.cs","后续适配"),
        new("Robot","ESTUN","Ethernet TCP","TCP","Robot/FormEstunTcp.cs","后续适配"),
        new("Robot","Hyundai","UDP","UDP","Robot/FormHyundaiUdp.cs","后续适配"),
        new("Robot","Efort","TCP","TCP","Robot/FormEfort*.cs","后续适配"),
        new("Robot","Yamaha","RCX","TCP","Robot/FormYamahaRCX.cs","后续适配"),
        new("CNC","Fanuc / CNC","CNC Gateway","TCP","CNC/Form*.cs","后续适配"),
        new("Instrument","RKC","Temperature Controller","TCP / Serial","Instrument/FormRkc*.cs","后续适配"),
        new("Sensor","Vibration / RFID","Sensor Bridge","TCP / Serial","Sensor/Form*.cs","后续适配"),
        new("Light","Industrial Light","Controller","TCP / Serial","Light/Form*.cs","后续适配")
    ];
    public ObservableCollection<ProtocolCatalogEntry> FilteredProtocols{get;}=new();
    public int ProtocolCount=>protocolCatalog.Length;
    [ObservableProperty] private string protocolSearch="";
    [ObservableProperty] private ProtocolCatalogEntry? selectedProtocol;
    [ObservableProperty] private int workspaceIndex;
    public bool IsModbusWorkspace=>WorkspaceIndex<8;
    public string WorkspaceTitle=>WorkspaceIndex switch{8=>"PLC 模拟器",9=>"工业 PLC 读写",10=>"协议迁移目录",_=>"Modbus TCP"};
    partial void OnWorkspaceIndexChanged(int value){OnPropertyChanged(nameof(IsModbusWorkspace));OnPropertyChanged(nameof(WorkspaceTitle));}
    [RelayCommand] private void SelectWorkspace(string index){if(int.TryParse(index,out var value))WorkspaceIndex=value;}
    [RelayCommand] private void OpenCatalogProtocol(){
        if(SelectedProtocol is not {} item)return;
        int protocol=item.Vendor switch{"Mitsubishi"=>0,"Siemens"=>1,"Omron"=>2,_=>-1};
        if(protocol>=0){
            if(!CanChangeIndustrial){Status="请先停止当前 PLC 连接及服务端再切换协议。";return;}
            IndustrialProtocolIndex=protocol;WorkspaceIndex=9;return;
        }
        if(item.Vendor=="Schneider / Modbus"){WorkspaceIndex=0;return;}
        Status=$"{item.Vendor}：{item.Status}，详见对应源码入口。";
    }

    private void InitializeProtocolCatalog(){
        FilterProtocols();
    }
    partial void OnProtocolSearchChanged(string value)=>FilterProtocols();
    private void FilterProtocols(){
        var query=ProtocolSearch.Trim();
        FilteredProtocols.Clear();
        foreach(var item in protocolCatalog.Where(item=>string.IsNullOrEmpty(query)||
            $"{item.Category} {item.Vendor} {item.Product} {item.Transport}".Contains(query,StringComparison.OrdinalIgnoreCase)))
            FilteredProtocols.Add(item);
    }
}
