using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

internal sealed class CanonicalCrashBoundary(CommitStage stage, int mutationIndex)
{
    private bool armed;

    /// <summary>Makes the seeded state durable, as an acknowledgement would, and then arms the crash boundary.</summary>
    internal async Task ArmAsync(IAtomicStore store)
    {
        await store.SyncAsync();
        armed = true;
    }

    internal void Observe(CommitStage observed, long _, int index)
    {
        if (armed && observed == stage
            && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            armed = false;
            RecoveryProcessPause.AtBoundary();
        }
    }
}
