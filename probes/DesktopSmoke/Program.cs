using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xbr.Desktop;
using Xbr.Desktop.Views;
using Xbr.Desktop.ViewModels;
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
  foreach(var (index,name) in new[]{(3,"arc-special"),(4,"arc-station-search")}) {
   tabs.SelectedIndex=index;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
   using var frame=window.CaptureRenderedFrame()??throw new Exception("No frame");frame.Save($"docs/baseline/screenshots/{name}.png",new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  }
  vm.IsConnected=false;
  Console.WriteLine("Arc window rendered: register, FC23, search; disconnected/connected/search states passed");window.Close();Dispatcher.UIThread.RunJobs();
 }
}
