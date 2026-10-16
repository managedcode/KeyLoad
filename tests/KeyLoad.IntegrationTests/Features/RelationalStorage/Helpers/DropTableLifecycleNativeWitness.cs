using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleNativeWitness
{
    internal static Task<DropLifecycleRf3ReceiptProof> CaptureAsync(string joinedRoot,
        DropTableLifecycleTable table, Guid parentId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(table);
        if (parentId == Guid.Empty || !table.Inventory.Complete)
        {
            throw DropTableLifecycleWitnessHistory.Invalid();
        }
        var owner = table.Inventory.Control.Directory.ControlOwner;
        var options = new ZoneTreeStoreOptions(joinedRoot)
            .ResolveExecutionOptions(IntegrationExecutionOptions.StorageExecution());
        var work = new ZoneTreeConservationBudget(options, TimeProvider.System, token);
        DropLifecycleRf3ReceiptProof? result = null;
        foreach (var voter in owner.VoterIds)
        {
            work.Check();
            var node = TwoRf3MembershipProtocol.Nodes.Single(candidate =>
                voter == "http://" + candidate + ":8080");
            var proof = DropTableLifecycleWitnessOwner.Read(joinedRoot, node, voter, owner,
                table, parentId, work, token);
            if (proof is null)
            {
                continue;
            }
            if (result is not null && result != proof)
            {
                throw DropTableLifecycleWitnessHistory.Invalid();
            }
            result = proof;
        }
        work.Check();
        return Task.FromResult(result ?? throw Errors.Fail(ErrorCode.RecoveryRequired,
            "No retained committed original DROP finalization entry is available on the stopped source voters."));
    }
}
