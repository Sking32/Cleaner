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

        if (string.IsNullOrWhiteSpace(op.Handler))
            return CleanupOperationResult.Fail($"{op.Key}: handler is empty");

        if (!_handlers.TryGetValue(op.Handler!, out var handler))
            return CleanupOperationResult.Fail($"{op.Key}: handler not registered: {op.Handler}");

        try
        {
            return handler.Run(op, ctx) ?? CleanupOperationResult.Fail($"{op.Key}: handler returned null");
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{op.Key}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------

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