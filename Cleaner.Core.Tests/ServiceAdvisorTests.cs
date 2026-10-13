using System.IO;
using System.Linq;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class ServiceAdvisorTests
{
    [Fact]
    public void Protected_Service_Is_System()
    {
        Assert.Equal(ServiceAdviceLevel.System, ServiceAdvisor.Advise("RpcSs").Level);
    }

    [Fact]
    public void Telemetry_Service_Is_Recommended_For_Disabling()
    {
        Assert.Equal(ServiceAdviceLevel.Recommended, ServiceAdvisor.Advise("DiagTrack").Level);
    }

    [Fact]
    public void Search_Service_Needs_Caution()
    {
        Assert.Equal(ServiceAdviceLevel.Caution, ServiceAdvisor.Advise("WSearch").Level);
    }

    [Fact]
    public void Unknown_Or_Empty_Service_Has_No_Advice()
    {
        Assert.Equal(ServiceAdviceLevel.None, ServiceAdvisor.Advise("SomeRandomService").Level);
        Assert.Equal(ServiceAdviceLevel.None, ServiceAdvisor.Advise("").Level);
    }

    [Fact]
    public void Every_Note_Key_Is_Present_In_Localization()
    {
        var dir = System.AppContext.BaseDirectory;
        string? path = null;
        for (var i = 0; i < 10 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "Cleaner.Core", "Data", "localization", "ru.json");
            if (File.Exists(candidate)) { path = candidate; break; }
            var direct = Path.Combine(dir, "Data", "localization", "ru.json");
            if (File.Exists(direct)) { path = direct; break; }
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(path);
        var json = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(path!))!;
        var missing = ServiceAdvisor.AllNoteKeys.Where(k => !json.ContainsKey(k)).ToList();
        Assert.Empty(missing);
    }
}
