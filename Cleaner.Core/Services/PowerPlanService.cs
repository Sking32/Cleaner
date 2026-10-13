using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cleaner.Core.Services;

public enum PowerPlanKind { Custom, Balanced, HighPerformance, PowerSaver }

public sealed record PowerPlan(Guid Id, string Name, bool IsActive, PowerPlanKind Kind);

/// <summary>Схемы электропитания Windows через powercfg.</summary>
public static class PowerPlanService
{
    private static readonly Guid BalancedId = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformanceId = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid PowerSaverId = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private static readonly Regex PlanLine = new(
        @"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s+\((.*?)\)\s*(\*)?",
        RegexOptions.Compiled);

    public static IReadOnlyList<PowerPlan> GetPlans()
    {
        try
        {
            var result = CommandRunner.Run("powercfg.exe", "/list", 15000);
            if (!result.Success)
            {
                CoreLog.Warn("powercfg /list завершился с ошибкой: " + result.StdErr.Trim());
                return Array.Empty<PowerPlan>();
            }

            return Parse(result.StdOut);
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Не удалось получить схемы электропитания", ex);
            return Array.Empty<PowerPlan>();
        }
    }

    public static IReadOnlyList<PowerPlan> Parse(string output)
    {
        var plans = new List<PowerPlan>();
        if (string.IsNullOrWhiteSpace(output)) return plans;

        foreach (var line in output.Split('\n'))
        {
            var match = PlanLine.Match(line);
            if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var id)) continue;

            var kind = id == BalancedId ? PowerPlanKind.Balanced
                : id == HighPerformanceId ? PowerPlanKind.HighPerformance
                : id == PowerSaverId ? PowerPlanKind.PowerSaver
                : PowerPlanKind.Custom;

            plans.Add(new PowerPlan(id, match.Groups[2].Value.Trim(), match.Groups[3].Success, kind));
        }

        return plans;
    }

    public static bool SetActive(Guid id)
    {
        try
        {
            return CommandRunner.Run("powercfg.exe", "/setactive " + id, 15000).Success;
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Не удалось переключить схему электропитания", ex);
            return false;
        }
    }
}
