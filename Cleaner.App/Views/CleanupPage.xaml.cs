using System.Windows.Controls;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class CleanupPage : Page
{
    public CleanupPage(CleanupViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}