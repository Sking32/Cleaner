using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup;

/// <summary>
/// Обработчик одной cleanup-операции. Соответствует полю "handler" в operations.json.
/// </summary>
public interface ICleanupHandler
{
    /// <summary>Имя handler'а (совпадает со строкой в operations.json).</summary>
    string Name { get; }

    /// <summary>Выполнить операцию.</summary>
    CleanupOperationResult Run(OperationEntry op, CleanupContext ctx);
}