using System.Windows.Controls;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();
    public SettingsPage(SettingsViewModel vm) : this() => DataContext = vm;
}
