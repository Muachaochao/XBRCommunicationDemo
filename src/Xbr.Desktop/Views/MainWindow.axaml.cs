using Avalonia.Controls;
namespace Xbr.Desktop.Views;
public partial class MainWindow : Window
{
    private bool closed;
    public MainWindow(){InitializeComponent();Closing+=async(_,e)=>{if(closed)return;e.Cancel=true;if(DataContext is Xbr.Desktop.ViewModels.MainViewModel vm)await vm.CloseAsync();closed=true;Close();};}
}
