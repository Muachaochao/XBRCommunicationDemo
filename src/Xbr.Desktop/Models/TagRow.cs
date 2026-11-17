using CommunityToolkit.Mvvm.ComponentModel;
using Xbr.Communication;
namespace Xbr.Desktop.Models;
public partial class TagRow:ObservableObject
{
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public string[] AvailableTypes {get;}=["bool","byte","short","ushort","int","uint","long","ulong","float","double","string"];
    public string[] AvailableEncodings {get;}=["ASCII","ANSI","UTF8","UTF16","UTF16Big","GB2312"];
    [ObservableProperty] private string name="点位";
    [ObservableProperty] private string address="100";
    [ObservableProperty] private string dataTypeCode="ushort";
    [ObservableProperty] private string length="";
    [ObservableProperty] private string encoding="ASCII";
    [ObservableProperty] private string expression="";
    [ObservableProperty] private string unit="";
    [ObservableProperty] private string description="";
    [ObservableProperty] private string value="";
    [ObservableProperty] private string lastUpdated="";
    [ObservableProperty] private string error="";
    [ObservableProperty] private bool isPlotted;
    public TagDefinition Definition(){
        var tag=new TagDefinition(Name,Address,DataTypeCode.Trim().ToLowerInvariant(),string.IsNullOrWhiteSpace(Length)?-1:int.Parse(Length),Encoding,Expression,Unit,Description);tag.Validate();return tag;
    }
    public static TagRow From(TagDefinition t)=>new(){Name=t.Name,Address=t.Address,DataTypeCode=t.DataTypeCode,Length=t.Length==-1?"":t.Length.ToString(),Encoding=t.StringEncoding,Expression=t.Expression,Unit=t.Unit,Description=t.Description};
}
