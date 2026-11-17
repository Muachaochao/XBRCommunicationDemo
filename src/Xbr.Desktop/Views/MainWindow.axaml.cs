using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using System.ComponentModel;
using System.Reflection;
using TKCode.Arc;
using Xbr.Desktop.Models;
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
        var tagGrid=this.FindControl<DataGrid>("TagGrid")??throw new InvalidOperationException("TagGrid was not loaded.");
        tagGrid.DoubleTapped+=TagGrid_DoubleTapped;
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
        ApplyCurveStyle();
        SyncCurveSeries();
    }
    private void ViewModel_PropertyChanged(object? sender,PropertyChangedEventArgs e){
        if(e.PropertyName==nameof(MainViewModel.CurveSeries))SyncCurveSeries();
        if(e.PropertyName is nameof(MainViewModel.CurveSmooth) or nameof(MainViewModel.CurveStrokeWidth))ApplyCurveStyle();
    }
    private void SyncCurveSeries(){
        if(boundViewModel is null)return;
        var desired=boundViewModel.CurveSeries;
        if(curveChart.Series.Count==desired.Count&&
           Enumerable.Range(0,desired.Count).All(index=>ReferenceEquals(curveChart.Series[index],desired[index])))return;
        curveChart.Series.Clear();
        foreach(var series in desired)curveChart.Series.Add(series);
    }
    private void ApplyCurveStyle(){
        if(boundViewModel is null)return;
        curveChart.Curve=boundViewModel.CurveSmooth?ChartCurve.Smooth:ChartCurve.Linear;
        var property=typeof(ChartBase).GetField("StrokeWidthProperty",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) as Avalonia.StyledProperty<double>;
        if(property is not null)curveChart.SetValue(property,Math.Clamp(boundViewModel.CurveStrokeWidth,1,8));
    }
    private async void TagGrid_DoubleTapped(object? sender,TappedEventArgs e){
        if(boundViewModel is null)return;
        var row=(e.Source as Control)?.DataContext as TagRow??(sender as DataGrid)?.SelectedItem as TagRow;
        if(row is null)return;
        var input=new TextBox{Text=row.Value,PlaceholderText="输入新值；数组用逗号分隔"};
        var resultText=new TextBlock{Text=row.Error,Foreground=Avalonia.Media.Brushes.IndianRed,TextWrapping=Avalonia.Media.TextWrapping.Wrap};
        var dialog=new Window{Title=$"写入 {CurveName(row)}",Width=480,Height=220,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var ok=new Avalonia.Controls.Button{Content="写入",IsDefault=true};
        var cancel=new Avalonia.Controls.Button{Content="取消",IsCancel=true};
        ok.Click+=(_,_)=>dialog.Close(input.Text??"");
        cancel.Click+=(_,_)=>dialog.Close(null);
        dialog.Content=new StackPanel{
            Margin=new Avalonia.Thickness(20),Spacing=12,
            Children={
                new TextBlock{Text=$"{row.Address} · {row.DataTypeCode} · {row.Unit}",Foreground=Avalonia.Media.Brushes.LightSteelBlue},
                input,
                resultText,
                new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Children={cancel,ok}}
            }
        };
        var value=await dialog.ShowDialog<string?>(this);
        if(value is not null)await boundViewModel.WriteTagValueAsync(row,value);
    }
    private static string CurveName(TagRow row)=>string.IsNullOrWhiteSpace(row.Name)?row.Address:row.Name;
}
