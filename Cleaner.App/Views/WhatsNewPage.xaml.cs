using System.Windows.Controls;
using Cleaner.App.ViewModels;
namespace Cleaner.App.Views;
public partial class WhatsNewPage : Page
{
 public WhatsNewPage() => InitializeComponent();
 public WhatsNewPage(WhatsNewViewModel vm) { InitializeComponent(); DataContext = vm; }
}
