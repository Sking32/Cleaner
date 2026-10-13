using Cleaner.App.Localization;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace Cleaner.App.ViewModels;

public sealed record ReleaseGroup(string Title, IReadOnlyList<string> Items)
{
    public bool HasItems => Items.Count > 0;
}
public sealed record ReleaseItem(string Version, string DateText, string Title, string Summary, bool IsCurrent, IReadOnlyList<ReleaseGroup> Groups);

public partial class WhatsNewViewModel : ObservableObject
{
    public string CurrentVersion => "Cleaner " + AppVersion.Current;
    public IReadOnlyList<ReleaseItem> Releases { get; }
    [ObservableProperty] private ReleaseItem? _selectedRelease;
    public string ErrorText { get; } = "";
    public bool HasError => ErrorText.Length > 0;

    public WhatsNewViewModel()
    {
        try
        {
            Releases = ReleaseCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "releases.json"), AppVersion.Current)
                .Select(r =>
                {
                    var text = r.Text[Loc.Language];
                    return new ReleaseItem(r.Version, r.Date ?? Loc.T("news.baseline"), text.Title, text.Summary, r.Version == AppVersion.Current,
                        new[] { new ReleaseGroup(Loc.T("news.added"), text.Added), new ReleaseGroup(Loc.T("news.changed"), text.Changed), new ReleaseGroup(Loc.T("news.fixed"), text.Fixed) });
                }).ToArray();
            SelectedRelease = Releases.First(r => r.IsCurrent);
        }
        catch (Exception ex) { Releases = Array.Empty<ReleaseItem>(); ErrorText = Loc.T("news.load.failed") + " " + ex.Message; }
    }
}
