using CommunityToolkit.Mvvm.ComponentModel;
using Xbr.Communication;

namespace Xbr.Desktop.Models;

public partial class BatchParseRow:ObservableObject
{
    [ObservableProperty] private string name="value";
    [ObservableProperty] private string type="ushort";
    [ObservableProperty] private string length="";
    [ObservableProperty] private string byteIndex="0";
    [ObservableProperty] private string value="";
    [ObservableProperty] private string error="";

    public BatchParseDefinition Definition(){
        if(!int.TryParse(ByteIndex,out var index)||index<0)throw new ArgumentException("字节索引必须是非负整数。");
        return new(Name,Type,Length,index);
    }
}
