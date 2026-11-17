using HslCommunication;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Delta.Helper;
using HslCommunication.Profinet.Inovance;
using HslCommunication.Profinet.MegMeet;
using HslCommunication.Profinet.XINJE;

namespace Xbr.Communication;

public static class ModbusAddressMappings
{
    public static readonly string[] Names=[
        "No Address Mapping","Inovance AM","Inovance H3U","Inovance H5U/Easy","Inovance EVO",
        "Delta AS","Delta DVP","MegMeet","XinJE XC","XinJE XD/XL","WeCon LX5V","INVT TS"];

    public static Func<string,byte,OperateResult<string>>? Resolve(string name)=>name switch{
        "Inovance AM"=>InovanceHelper.PraseInovanceAMAddress,
        "Inovance H3U"=>InovanceHelper.PraseInovanceH3UAddress,
        "Inovance H5U/Easy"=>InovanceHelper.PraseInovanceH5UAddress,
        "Inovance EVO"=>InovanceHelper.PraseInovanceEvoAddress,
        "Delta AS"=>ModbusMappingAddress.Delta_AS,
        "Delta DVP"=>DeltaDvpHelper.ParseDeltaDvpAddress,
        "MegMeet"=>MegMeetHelper.PraseMegMeetAddress,
        "XinJE XC"=>((address,station)=>XinJEHelper.PraseXinJEAddress(XinJESeries.XC,address,station)),
        "XinJE XD/XL"=>((address,station)=>XinJEHelper.PraseXinJEAddress(XinJESeries.XD,address,station)),
        "WeCon LX5V"=>ModbusMappingAddress.WeCon_Lx5v,
        "INVT TS"=>ModbusMappingAddress.Invt_Ts,
        _=>null
    };
}
