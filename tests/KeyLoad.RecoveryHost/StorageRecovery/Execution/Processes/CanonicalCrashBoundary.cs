using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryHost;

internal sealed class CanonicalCrashBoundary(CommitStage stage, int mutationIndex, bool initiallyArmed)
{
    internal bool Armed { get; set; } = initiallyArmed;
    internal long Position { get; set; } = CrashFixtureValues.InitialCrashPosition;

    internal void Observe(CommitStage observed, long position, int index)
    {
        if (Armed && position == Position && observed == stage
            && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            RecoveryHostPause.AtBoundary();
        }
    }
}
