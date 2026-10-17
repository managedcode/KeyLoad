using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionScopedOutcome
{
    internal static bool Exists(IKeyValueView view, PartitionRef partition, string principalId, Guid commandId)
    {
        var expected = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, partition);
        return CommandOutcomeKeyResolver.Select(view, principalId, commandId, expected).Outcome is not null;
    }
}
