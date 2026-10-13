namespace Cleaner.Core.Services;

public readonly record struct QuarantineSweepResult(int DeletedSessions, int FailedSessions,
    IReadOnlyList<string> Errors, bool Canceled);

public sealed partial class QuarantineService
{
    public static QuarantineSweepResult ClearOldQuarantineDetailed(int olderThanDays, string? rootDirectory = null,
        CancellationToken cancellationToken = default, IProgress<QuarantineProgress>? progress = null)
    {
        if (cancellationToken.IsCancellationRequested) return new(0, 0, Array.Empty<string>(), true);
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(olderThanDays, 0));
        var candidates = InspectSessions(rootDirectory).Where(s => s.CreatedUtc < cutoff).ToArray();
        int deleted = 0, failed = 0, completed = 0;
        var errors = new List<string>();
        foreach (var session in candidates)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!session.CanDelete)
            {
                failed++;
                errors.Add(session.Id + ": " + string.Join("; ", session.Issues.Take(3)));
            }
            else
            {
                try
                {
                    var result = new QuarantineService(session.Directory).ClearAllDetailed(cancellationToken);
                    if (result.Canceled) return new(deleted, failed, errors, true);
                    if (result.Errors.Count == 0) deleted++;
                    else { failed++; errors.AddRange(result.Errors.Select(e => session.Id + ": " + e)); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { failed++; errors.Add(session.Id + ": " + ex.Message); }
            }
            progress?.Report(new(++completed, candidates.Length, session.Id));
        }
        return new(deleted, failed, errors, cancellationToken.IsCancellationRequested);
    }
}
