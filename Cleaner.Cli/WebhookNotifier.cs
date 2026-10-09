using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cleaner.Core.Services.Cleanup;

namespace Cleaner.Cli;

/// <summary>
/// Отправляет итог cleanup-сессии на webhook (POST, JSON).
/// Ошибки сети не считаются фатальными.
/// </summary>
public static class WebhookNotifier
{
    public static async Task<bool> SendAsync(
        string url,
        CleanupSessionResult result,
        string source,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            var payload = new
            {
                source,
                when = DateTime.UtcNow.ToString("o"),
                bytesFreed = result.BytesFreed,
                filesDeleted = result.FilesDeleted,
                filesSkipped = result.FilesSkipped,
                operationsRun = result.OperationsRun,
                operationsFailed = result.OperationsFailed,
                durationMs = (long)result.Duration.TotalMilliseconds,
                canceled = result.Canceled,
                outcomes = result.Outcomes
            };

            var json = JsonSerializer.Serialize(payload);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}