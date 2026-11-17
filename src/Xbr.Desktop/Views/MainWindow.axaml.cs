using Avalonia.Controls;
using System.ComponentModel;
using TKCode.Arc;
using Xbr.Desktop.ViewModels;
namespace Xbr.Desktop.Views;
public partial class MainWindow : Window
{
    private bool closed;
    private MainViewModel? boundViewModel;
    private readonly LineChart curveChart;
    public MainWindow(){
        InitializeComponent();
        curveChart=this.FindControl<LineChart>("CurveChart")??throw new InvalidOperationException("CurveChart was not loaded.");
        DataContextChanged+=(_,_)=>AttachViewModel(DataContext as MainViewModel);
        Closing+=async(_,e)=>{
            if(closed)return;e.Cancel=true;
            if(DataContext is MainViewModel vm)await vm.CloseAsync();
            closed=true;Close();
        };
    }
    private void AttachViewModel(MainViewModel? viewModel){
        if(boundViewModel is not null)boundViewModel.PropertyChanged-=ViewModel_PropertyChanged;
        boundViewModel=viewModel;
        if(boundViewModel is not null)boundViewModel.PropertyChanged+=ViewModel_PropertyChanged;
        SyncCurveSeries();
    }
    private void ViewModel_PropertyChanged(object? sender,PropertyChangedEventArgs e){
        if(e.PropertyName==nameof(MainViewModel.CurveSeries))SyncCurveSeries();
    }
    private void SyncCurveSeries(){
        if(boundViewModel is null)return;
        var desired=boundViewModel.CurveSeries;
        if(curveChart.Series.Count==desired.Count&&
           Enumerable.Range(0,desired.Count).All(index=>ReferenceEquals(curveChart.Series[index],desired[index])))return;
        curveChart.Series.Clear();
        foreach(var series in desired)curveChart.Series.Add(series);
    }
}
