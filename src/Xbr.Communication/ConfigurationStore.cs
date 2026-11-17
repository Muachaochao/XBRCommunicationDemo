using System.Xml.Linq;

namespace Xbr.Communication;

public static class ConfigurationStore
{
    // Preserve unhandled original attributes/children when saving imported plain XML.
    public static XElement Save(ConnectionOptions o, XElement? original = null) {
        var e=original is null ? new XElement("Device") : new XElement(original);
        foreach(var pair in new Dictionary<string,object> {
            ["Type"]="FormModbus",["PipeSetting"]="TcpPipe",["IP"]=o.Host,["Port"]=o.Port,
            ["ConnectTimeOut"]=o.ConnectTimeout,["ReceiveTimeOut"]=o.ReceiveTimeout,["Station"]=o.Station,
            ["AddressStartWithZero"]=o.AddressStartWithZero,["DataFormat"]=o.DataFormat,["StringReverse"]=o.StringReverse,
            ["StationCheckMatch"]=o.StationCheck,["DisableFunctionCode06"]=o.DisableFunctionCode06,
            ["BroadcastStation"]=o.BroadcastStation==-1?"":o.BroadcastStation.ToString(),["WordReadBatchLength"]=o.WordReadBatchLength
        }) e.SetAttributeValue(pair.Key,pair.Value);
        return e;
    }
    public static ConnectionOptions Load(XElement e) {
        string Get(string key,string fallback)=>e.Attribute(key)?.Value ?? fallback;
        if(e.Attribute("Encrypt")!=null || e.Attribute("Encrypt2")!=null) throw new NotSupportedException("加密配置尚未迁移，请保留原文件并使用原版打开。");
        if(Get("PipeSetting","TcpPipe")!="TcpPipe") throw new NotSupportedException("当前页面仅支持 TcpPipe。");
        if(Get("Type","FormModbus")!="FormModbus") throw new NotSupportedException("请选择 FormModbus 的设备配置元素。");
        int Optional(string key,int fallback)=>string.IsNullOrWhiteSpace(Get(key,""))?fallback:int.Parse(Get(key,""));
        var o=new ConnectionOptions { Host=Get("IP","127.0.0.1"),Port=Optional("Port",502),Station=byte.Parse(Get("Station","1")),
            ConnectTimeout=Optional("ConnectTimeOut",5000),ReceiveTimeout=Optional("ReceiveTimeOut",10000),
            DataFormat=Optional("DataFormat",2),AddressStartWithZero=bool.Parse(Get("AddressStartWithZero","true")),
            StringReverse=bool.Parse(Get("StringReverse","false")),StationCheck=bool.Parse(Get("StationCheckMatch","true")),
            DisableFunctionCode06=bool.Parse(Get("DisableFunctionCode06","false")),BroadcastStation=Optional("BroadcastStation",-1),
            WordReadBatchLength=Optional("WordReadBatchLength",120) }; o.Validate(); return o;
    }
}
