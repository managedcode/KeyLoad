using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.BackupRestore.Execution;

/// <summary>Preserves roster publication context while the native provider owns a discard-only child callback.</summary>
internal static class AtomicPartitionRosterPreparation
{
    private const string PreparationDuringCleanupUnsupported = "Retirement cleanup cannot fork preparation state.";

    internal static T Prepare<T>(IAtomicTransaction inner, IOptions<DatabaseLimits> limits,
        long storePosition, long appliedIndex, bool cleanupActive, Func<IAtomicTransaction, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (cleanupActive)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PreparationDuringCleanupUnsupported);
        }
        return inner.Prepare(child => prepare(new AtomicPartitionRosterTransaction(child, limits,
            storePosition, appliedIndex)));
    }
}
