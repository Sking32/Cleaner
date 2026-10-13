using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

/// <summary>Если карантин не принял файл — файл НЕ должен удаляться безвозвратно.</summary>
public class PathCleanupQuarantineFailureTests : IDisposable
{
    private sealed class FailingQuarantine : IQuarantineService
    {
        public string SessionDirectory => "";
        public int Count => 0;
        public long TotalSizeBytes => 0;
        public IReadOnlyList<QuarantineEntry> Entries => Array.Empty<QuarantineEntry>();
        public QuarantineEntry? MoveToQuarantine(string path, string category) => null;
        public QuarantineRestoreResult RestoreAll() => throw new NotSupportedException();
        public int ClearAll() => throw new NotSupportedException();
    }

    private readonly string _root;
    private readonly string _dir;

    public PathCleanupQuarantineFailureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerQF_" + Guid.NewGuid().ToString("N"));
        _dir = Path.Combine(_root, "temp");
        Directory.CreateDirectory(_dir);
        Directory.SetCreationTimeUtc(_dir, DateTime.UtcNow.AddMinutes(-30));
        Directory.SetCreationTimeUtc(_root, DateTime.UtcNow.AddMinutes(-30));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void File_Survives_When_Quarantine_Fails()
    {
        var file = Path.Combine(_dir, "keep.tmp");
        File.WriteAllText(file, "data");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddMinutes(-30));

        var ctx = new CleanupContext(
            new SafetyService(freshMinutes: 5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new FailingQuarantine());

        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "test",
            Handler = "PathCleanup",
            Paths = new List<string> { _dir }
        };

        var result = new PathCleanupHandler().Run(op, ctx);

        Assert.True(File.Exists(file));
        Assert.Equal(0, result.FilesQuarantined);
        Assert.Equal(1, result.FilesSkipped);
    }
}
