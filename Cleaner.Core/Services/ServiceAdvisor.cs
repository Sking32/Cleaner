using System;
using System.Collections.Generic;

namespace Cleaner.Core.Services;

public enum ServiceAdviceLevel
{
    None,
    /// <summary>Службу обычно можно безопасно отключить.</summary>
    Recommended,
    /// <summary>Отключение может повлиять на функции Windows — нужна осторожность.</summary>
    Caution,
    /// <summary>Ключевая системная служба — трогать нельзя.</summary>
    System
}

public readonly record struct ServiceAdvice(ServiceAdviceLevel Level, string? NoteKey);

/// <summary>Подсказки по службам Windows: что можно отключить, а что трогать нельзя.</summary>
public static class ServiceAdvisor
{
    private static readonly Dictionary<string, string> Recommended = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DiagTrack"] = "svc.note.diagtrack",
        ["dmwappushservice"] = "svc.note.dmwappushservice",
        ["MapsBroker"] = "svc.note.mapsbroker",
        ["RetailDemo"] = "svc.note.retaildemo",
        ["Fax"] = "svc.note.fax",
        ["XblAuthManager"] = "svc.note.xbox",
        ["XblGameSave"] = "svc.note.xbox",
        ["XboxNetApiSvc"] = "svc.note.xbox",
        ["WerSvc"] = "svc.note.wersvc",
        ["RemoteRegistry"] = "svc.note.remoteregistry",
        ["lfsvc"] = "svc.note.lfsvc",
        ["WMPNetworkSvc"] = "svc.note.wmpnetworksvc",
        ["TrkWks"] = "svc.note.trkwks",
        ["WpcMonSvc"] = "svc.note.wpcmonsvc"
    };

    private static readonly Dictionary<string, string> Caution = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SysMain"] = "svc.note.sysmain",
        ["WSearch"] = "svc.note.wsearch",
        ["Spooler"] = "svc.note.spooler",
        ["wuauserv"] = "svc.note.wuauserv",
        ["bthserv"] = "svc.note.bthserv",
        ["BITS"] = "svc.note.bits",
        ["LanmanServer"] = "svc.note.lanmanserver",
        ["TermService"] = "svc.note.termservice",
        ["Audiosrv"] = "svc.note.audiosrv",
        ["AudioEndpointBuilder"] = "svc.note.audiosrv"
    };

    public static IEnumerable<string> AllNoteKeys
    {
        get
        {
            var keys = new HashSet<string> { "svc.note.system" };
            foreach (var k in Recommended.Values) keys.Add(k);
            foreach (var k in Caution.Values) keys.Add(k);
            return keys;
        }
    }

    public static ServiceAdvice Advise(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return new ServiceAdvice(ServiceAdviceLevel.None, null);

        if (ServiceHelper.IsProtected(serviceName))
            return new ServiceAdvice(ServiceAdviceLevel.System, "svc.note.system");

        if (Recommended.TryGetValue(serviceName.Trim(), out var rec))
            return new ServiceAdvice(ServiceAdviceLevel.Recommended, rec);

        if (Caution.TryGetValue(serviceName.Trim(), out var cau))
            return new ServiceAdvice(ServiceAdviceLevel.Caution, cau);

        return new ServiceAdvice(ServiceAdviceLevel.None, null);
    }
}
