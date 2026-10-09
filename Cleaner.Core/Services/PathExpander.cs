using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Cleaner.Core.Services;

/// <summary>
/// Разворачивает переменные окружения в путях из operations.json.
/// Поддерживает %TEMP%, %WINDIR%, %LOCALAPPDATA%, %APPDATA%, %PROGRAMDATA%,
/// %PROGRAMFILES%, %PROGRAMFILES(X86)%, %USERPROFILE% и любые системные %VAR%.
/// </summary>
public static class PathExpander
{
    private static readonly Regex VarRegex = new(
        @"%([A-Za-z0-9_()]+)%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Разворачивает один путь. Пустая строка → пустая строка.</summary>
    public static string Expand(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var result = VarRegex.Replace(path, m =>
        {
            var name = m.Groups[1].Value;
            var folder = TryGetSpecialFolder(name);
            if (!string.IsNullOrEmpty(folder)) return folder;
            // Fallback: стандартный ExpandEnvironmentVariables (для произвольных %VAR%)
            return Environment.ExpandEnvironmentVariables(m.Value);
        });

        // Убираем trailing slash, кроме корня диска (C:\)
        if (result.Length > 3)
            result = result.TrimEnd('\\', '/');

        return result;
    }

    /// <summary>Разворачивает набор путей.</summary>
    public static IReadOnlyList<string> ExpandAll(IEnumerable<string>? paths)
    {
        if (paths == null) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var p in paths)
        {
            var expanded = Expand(p);
            if (expanded.Length > 0) list.Add(expanded);
        }
        return list;
    }

    /// <summary>Есть ли в пути символы подстановки * или ?.</summary>
    public static bool ContainsWildcard(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0;
    }

    // ------------------------------------------------------------------

    private static string TryGetSpecialFolder(string name)
    {
        var upper = name.ToUpperInvariant();
        try
        {
            switch (upper)
            {
                case "TEMP":
                case "TMP":
                    {
                        var tmp = Path.GetTempPath();
                        return string.IsNullOrEmpty(tmp) ? "" : tmp.TrimEnd('\\', '/');
                    }

                case "WINDIR":
                case "SYSTEMROOT":
                    return Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                case "LOCALAPPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                case "APPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                case "PROGRAMDATA":
                case "ALLUSERSPROFILE":
                    return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

                case "USERPROFILE":
                    return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                case "PROGRAMFILES":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                case "PROGRAMFILES(X86)":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                case "SYSTEMDRIVE":
                    return Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            }
        }
        catch { /* неизвестный/битый путь */ }

        return "";
    }
}