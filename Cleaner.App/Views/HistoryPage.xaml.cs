using System.Windows.Controls;
using Cleaner.App.ViewModels;
namespace Cleaner.App.Views;
public partial class HistoryPage : Page
{
 public HistoryPage() => InitializeComponent();
 public HistoryPage(HistoryViewModel vm) { InitializeComponent(); DataContext = vm; Loaded += (_, _) => vm.RefreshCommand.Execute(null); }
}
