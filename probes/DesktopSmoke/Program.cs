using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xbr.Desktop;
using Xbr.Desktop.Views;
using Xbr.Desktop.ViewModels;
using Xbr.Desktop.Models;
using TKCode.Arc;

internal static class Program {
 [STAThread] static void Main() {
  AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().UseArc(a=>a.UseArcControls().WithTheme(t=>t.Appearance(ArcAppearance.Dark))).WithInterFont().SetupWithoutStarting();
  var vm=new MainViewModel();
  var window=new MainWindow{DataContext=vm};window.Show();
  Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
  var bitmap=window.CaptureRenderedFrame()??throw new Exception("No rendered frame");
  Directory.CreateDirectory("docs/baseline/screenshots");bitmap.Save("docs/baseline/screenshots/arc-modbus.png",new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  if(vm.CanOperate||!vm.CanEditConnection)throw new Exception("Disconnected state mismatch");
  vm.IsConnected=true;
  if(!vm.CanOperate||vm.CanEditConnection)throw new Exception("Connected state mismatch");
  vm.IsSearching=true;
  if(vm.CanOperate)throw new Exception("Searching did not disable normal operations");
  vm.IsSearching=false;
  var tabs=window.FindControl<TabControl>("WorkspaceTabs")!;
  // Rendering-only state, not a real device connection.
  vm.Status="布局预览 · 模拟连接状态";
  vm.BatchContent=string.Join(" ",Enumerable.Range(1,32).Select(x=>x.ToString("X2")));
  vm.BatchInfo="16 字节 · 预览 · 每行 8 字节";
  vm.BatchSearch="0A";vm.FindBatchTextCommand.Execute(null);
  if(vm.BatchSelectionStart<0||!vm.BatchSearchInfo.Contains("字节偏移"))throw new Exception("Batch text search failed");
  vm.BatchSearchRegex=true;vm.BatchSearch="0[1-3]";vm.FindBatchTextCommand.Execute(null);
  if(vm.BatchSelectionEnd<=vm.BatchSelectionStart)throw new Exception("Batch regex search failed");
  vm.BatchParseEnabled=true;
  vm.BatchParseRows.Add(new BatchParseRow{Name="pressure",Type="ushort",ByteIndex="0",Value="0.01"});
  vm.BatchParseRows.Add(new BatchParseRow{Name="raw",Type="byte",Length="4",ByteIndex="4",Value="01 02 03 04"});
  vm.BatchCode="OperateResult<byte[]> read = modbus.Read( \"100\", 10 );\n    ushort pressure = modbus.ByteTransform.TransUInt16( read.Content, 0 );";
  vm.Tags.Add(new TagRow{Name="压力",Address="100",DataTypeCode="ushort",Unit="kPa",Value="13.5",LastUpdated="12:00:00.000",IsPlotted=true,CurveColor="#4EA1F7"});
  vm.Tags.Add(new TagRow{Name="温度",Address="102",DataTypeCode="float",Unit="℃",Value="26.8",LastUpdated="12:00:00.000",IsPlotted=true,CurveColor="#C37BFF",CurveDashed=true});
  vm.TableStatus="2 项 · 2 成功 · 0 失败";
  var baseTime=new DateTime(2026,11,17,12,0,0);
  vm.CurveCategories=Enumerable.Range(0,12).Select(x=>baseTime.AddSeconds(x)).ToArray();
  vm.CurveSeries=[
   new ChartSeries{Key="pressure",Name="压力",Values=[12.1,12.4,12.8,12.6,13.0,13.4,13.2,13.5,13.7,13.4,13.8,14.0]},
   new ChartSeries{Key="temperature",Name="温度",Values=[25.2,25.4,25.7,25.9,26.1,26.2,26.4,26.5,26.7,26.6,26.8,26.9],IsDashed=true,Color=Avalonia.Media.Color.Parse("#C37BFF")}];
  vm.CurveTitle="实时曲线 · 2 个点位";
  vm.CurveSummary="已保留 12/4000 轮 · 5 分钟时间窗 · 两个标量点位同时显示";
  vm.CurveSmooth=true;vm.CurveStrokeWidth=3;vm.CurveWindowMinutes="5";
  var curveChart=window.FindControl<LineChart>("CurveChart")!;
  if(curveChart.Series.Count!=2)throw new Exception($"Curve series count mismatch: {curveChart.Series.Count}");
  foreach(var (index,name) in new[]{(3,"arc-special"),(4,"arc-station-search"),(5,"arc-batch-read"),(6,"arc-data-table"),(7,"arc-live-curve")}) {
   tabs.SelectedIndex=index;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
   if(index==7)
    for(var tick=0;tick<20;tick++){
     System.Threading.Thread.Sleep(25);
     AvaloniaHeadlessPlatform.ForceRenderTimerTick();
     Dispatcher.UIThread.RunJobs();
    }
   using var frame=window.CaptureRenderedFrame()??throw new Exception("No frame");frame.Save($"docs/baseline/screenshots/{name}.png",new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  }
  tabs.SelectedIndex=7;
  foreach(var (width,height,name) in new[]{(1000d,700d,"arc-minimum-1000x700"),(1600d,1000d,"arc-expanded-1600x1000")}){
   window.Width=width;window.Height=height;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
   using var frame=window.CaptureRenderedFrame()??throw new Exception($"No frame at {name}");frame.Save($"docs/baseline/screenshots/{name}.png",new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  }
  vm.IsConnected=false;
  Console.WriteLine($"Arc window rendered: register, FC23, search, batch parse/search/code, editable table, styled multi-series curve ({curveChart.Series.Count} series), minimum/expanded layout; states passed");window.Close();Dispatcher.UIThread.RunJobs();
 }
}
