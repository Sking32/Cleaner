using System;
using System.Collections.Generic;
using System.Linq;
using Cleaner.Core.Data;
using Cleaner.Core.Services.Cleanup.Handlers;

namespace Cleaner.Core.Services.Cleanup;

public interface ICleanupService
{
    /// <summary>Все зарегистрированные handlers по имени.</summary>
    IReadOnlyDictionary<string, ICleanupHandler> Handlers { get; }

    /// <summary>Выполнить одну операцию по ключу (temp_user, chrome, wupdate…).</summary>
    CleanupOperationResult RunOperation(string operationKey, CleanupContext ctx);

    /// <summary>Выполнить одну операцию по описанию.</summary>
    CleanupOperationResult RunOperation(OperationEntry op, CleanupContext ctx);

    /// <summary>Зарегистрировать (или заменить) handler.</summary>
    void Register(ICleanupHandler handler);
}

/// <summary>
/// Диспетчер cleanup-операций: смотрит на op.Handler и вызывает
/// соответствующий <see cref="ICleanupHandler"/>.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    private readonly OperationsRepository _operations;
    private readonly Dictionary<string, ICleanupHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICleanupHandler> Handlers => _handlers;

    public CleanupService(OperationsRepository operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        RegisterDefaults();
    }

    public void Register(ICleanupHandler handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        _handlers[handler.Name] = handler;
    }

    public CleanupOperationResult RunOperation(string operationKey, CleanupContext ctx)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
            return CleanupOperationResult.Fail("operationKey is empty");

        var op = _operations.GetByKey(operationKey);
        if (op == null)
            return CleanupOperationResult.Fail($"operation not found: {operationKey}");
        if (!op.IsOperation)
            return CleanupOperationResult.Fail($"{operationKey} is not an operation");

        return RunOperation(op, ctx);
    }

    public CleanupOperationResult RunOperation(OperationEntry op, CleanupContext ctx)
    {
        if (op == null) return CleanupOperationResult.Fail("operation is null");
        if (ctx == null) return CleanupOperationResult.Fail("context is null");
        if (!op.Enabled) return CleanupOperationResult.Fail("cleanup.rule.disabled");
        if (op.ExpertOnly && !ctx.ExpertMode) return CleanupOperationResult.Fail("compatibility.expert_required");
        if (ctx.Activity.IsBusy(op.Key))
        {
            ctx.RecordSkip(op.Key, "", "application_running");
            return new CleanupOperationResult { Success = true, FilesSkipped = 1, Message = "application_running" };
        }

        if (string.IsNullOrWhiteSpace(op.Handler))
            return CleanupOperationResult.Fail($"{op.Key}: handler is empty");

        if (!_handlers.TryGetValue(op.Handler!, out var handler))
            return CleanupOperationResult.Fail($"{op.Key}: handler not registered: {op.Handler}");

        try
        {
            ctx.ConfigureRule(op);
            if (ctx.CapturePlan) ctx.ApproveRoots(op.Key, PathExpander.ExpandAll(op.Paths));
            var result = handler.Run(op, ctx) ?? CleanupOperationResult.Fail($"{op.Key}: handler returned null");
            if (ctx.CapturePlan && result.Success && !IsFileHandler(op.Handler!)) ctx.RecordAction(op);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{op.Key}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    public static bool IsFileHandler(string handler) => handler is "PathCleanup" or "MultiPathCleanup" or "PathCleanupRecursive"
        or "BrowserProfiles" or "FirefoxProfiles" or "ObsLogs" or "JumpLists" or "SmartDownloads" or "ActivityHistory" or "ThumbnailCache";

    private void RegisterDefaults()
    {
        // Простые
        Register(new PathCleanupHandler());
        Register(new MultiPathCleanupHandler());
        Register(new PathCleanupRecursiveHandler());
        Register(new CommandHandler());

        // Приложения / браузеры
        Register(new BrowserProfilesHandler());
        Register(new FirefoxProfilesHandler());
        Register(new JumpListsHandler());
        Register(new ObsLogsHandler());

        // Кэши
        Register(new ThumbnailCacheHandler());
        Register(new FontCacheHandler());
        Register(new WindowsStoreCacheHandler());

        // Реестр
        Register(new RegCleanKeyHandler());
        Register(new RegCleanRunMRUHandler());
        Register(new ActivityHistoryHandler());

        // Тяжёлое
        Register(new SmartDownloadsHandler());
        Register(new WindowsUpdateCacheHandler());
        Register(new DismComponentCleanupHandler());
        Register(new WindowsOldHandler());
        Register(new ShadowCopyHandler());
        Register(new ToggleHibernationHandler());
    }
}
