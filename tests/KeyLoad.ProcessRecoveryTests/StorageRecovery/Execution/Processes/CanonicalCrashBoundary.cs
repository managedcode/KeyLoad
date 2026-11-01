using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

internal sealed class CanonicalCrashBoundary(CommitStage stage, int mutationIndex, bool initiallyArmed)
{
    internal bool Armed { get; set; } = initiallyArmed;

    /// <summary>Makes the seeded state durable, as an acknowledgement would, and then arms the crash boundary.</summary>
    internal async Task ArmAsync(IAtomicStore store)
    {
        await store.SyncAsync();
        Armed = true;
    }
    internal long Position { get; set; } = CrashFixtureValues.InitialCrashPosition;

    internal void Observe(CommitStage observed, long position, int index)
    {
        if (Armed && position == Position && observed == stage
            && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            RecoveryProcessPause.AtBoundary();
        }
    }
}
