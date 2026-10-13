using System.Windows.Controls;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class StartupPage : Page
{
    public StartupPage() => InitializeComponent();

    public StartupPage(StartupViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
