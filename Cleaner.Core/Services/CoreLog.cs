using System;

namespace Cleaner.Core.Services;

/// <summary>
/// Лёгкий журнал для Core. Сам никуда не пишет: приложение подключает свой
/// обработчик через <see cref="Sink"/> (уровень, сообщение, исключение).
/// Никогда не бросает исключений.
/// </summary>
public static class CoreLog
{
    public static Action<string, string, Exception?>? Sink { get; set; }

    public static void Info(string message) => Emit("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Emit("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Emit("ERROR", message, ex);

    private static void Emit(string level, string message, Exception? ex)
    {
        try { Sink?.Invoke(level, message, ex); }
        catch { /* журнал не должен ломать работу */ }
    }
}
