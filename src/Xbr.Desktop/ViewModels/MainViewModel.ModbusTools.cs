using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbr.Communication;

namespace Xbr.Desktop.ViewModels;

public partial class MainViewModel
{
    private CancellationTokenSource? searchCancellation;
    private Task? searchTask;
    [ObservableProperty] private string specialReadAddress="";
    [ObservableProperty] private string specialReadLength="";
    [ObservableProperty] private string specialWriteAddress="";
    [ObservableProperty] private string specialWriteHex="";
    [ObservableProperty] private string specialCode="";
    [ObservableProperty] private string searchStart="0";
    [ObservableProperty] private string searchEnd="255";
    [ObservableProperty] private string searchTimeout="500";
    [ObservableProperty] private string searchAddress="0";
    [ObservableProperty] private string searchLength="1";
    [ObservableProperty] private bool searchWord=true;
    [ObservableProperty] private bool stopOnMatch;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private string searchMatches="";
    [ObservableProperty] private string searchLog="";
    partial void OnIsSearchingChanged(bool value)=>Refresh();
    [RelayCommand] private Task ReadWrite()=>Run(async()=> {
        var length=ushort.Parse(SpecialReadLength);
        Result=await service.ReadWriteAsync(SpecialReadAddress,length,SpecialWriteAddress,SpecialWriteHex);
        SpecialCode=$"device.ReadWrite(\"{SpecialReadAddress}\", {length}, \"{SpecialWriteAddress}\", \"{SpecialWriteHex}\".ToHexBytes());";
        Log("FC23 读写结果 → "+Result);
    });
    [RelayCommand] private async Task SearchStations() {
        if(!CanOperate)return;
        searchCancellation?.Dispose();searchCancellation=new();
        IsSearching=true;SearchMatches="";SearchLog="";
        searchTask=Run(async()=> {
            try {
                await service.SearchStationsAsync(int.Parse(SearchStart),int.Parse(SearchEnd),int.Parse(SearchTimeout),SearchAddress,
                    ushort.Parse(SearchLength),SearchWord,StopOnMatch,new Progress<StationSearchResult>(r=> {
                        SearchLog+=$"站号 {r.Station}：{(r.Success?"有响应":"无有效响应")}\n";
                        if(r.Success)SearchMatches+=$"站号 {r.Station}：{r.Content}\n";
                    }),searchCancellation.Token);
                Log("站号搜索结束，连接参数已恢复");
            } catch(OperationCanceledException) {Log("站号搜索已停止，连接参数已恢复");}
            finally {IsSearching=false;}
        });
        await searchTask;
    }
    [RelayCommand] private async Task StopSearch() {
        searchCancellation?.Cancel();
        if(searchTask!=null)await searchTask;
    }
}
