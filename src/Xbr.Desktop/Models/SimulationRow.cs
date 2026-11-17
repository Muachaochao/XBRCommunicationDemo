using CommunityToolkit.Mvvm.ComponentModel;
using Xbr.Communication;
using System.Xml.Linq;

namespace Xbr.Desktop.Models;

public partial class SimulationRow:ObservableObject
{
    [ObservableProperty] private string name="点位";
    [ObservableProperty] private string address="100";
    [ObservableProperty] private string timeMilliseconds="100";
    [ObservableProperty] private string expression="x * 10";
    [ObservableProperty] private string currentValue="";
    [ObservableProperty] private string encoding="";
    [ObservableProperty] private string mark="";
    [ObservableProperty] private string error="";
    public string[] AvailableEncodings{get;}=["","ASCII","ANSI","UTF8","UTF16","UTF16Big","GB2312"];
    private XElement? imported;
    public XElement ToXml(){
        var element=imported is null?new XElement("SimulateWrite"):new XElement(imported);
        element.SetAttributeValue("Name",Name);element.SetAttributeValue("Address",Address);
        element.SetAttributeValue("Time",TimeMilliseconds);element.SetAttributeValue("Express",Expression);
        element.SetAttributeValue("Mark",Mark);element.SetAttributeValue("Encoding",Encoding);return element;
    }
    public static SimulationRow FromXml(XElement element)=>new(){
        imported=new XElement(element),Name=(string?)element.Attribute("Name")??"",
        Address=(string?)element.Attribute("Address")??"",TimeMilliseconds=(string?)element.Attribute("Time")??"100",
        Expression=(string?)element.Attribute("Express")??"",Mark=(string?)element.Attribute("Mark")??"",
        Encoding=(string?)element.Attribute("Encoding")??""
    };

    public ModbusSimulationDefinition Definition(){
        if(!int.TryParse(TimeMilliseconds,out var time)||time<0)throw new ArgumentException("周期须为非负整数。");
        if(string.IsNullOrWhiteSpace(Address)||string.IsNullOrWhiteSpace(Expression))throw new ArgumentException("地址和表达式不能为空。");
        return new(Name,Address,time,Expression,Encoding);
    }
}
