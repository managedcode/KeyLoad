using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementFencedAbortRf3Assertions
{
    private const int Version = 1;
    private const int ControlVoters = 3;
    private const long NoOutstandingGrants = 0;

    internal static async Task UnchangedAsync(PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        foreach (var seed in seeds)
        {
            await seed.VerifyAsync(token);
            var current = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
                new(Version, seed.Partition), token));
            await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, current);
            var mcp = await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await seed.Official.CallAsync(AtomicPartitionPlacementPublicRf3Protocol.ReadTool,
                    new AtomicPartitionPlacementReadRequest(Version, seed.Partition), token));
            await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, mcp.Value);
        }
    }

    internal static async Task SecondaryAsync(TwoRf3MembershipWave wave, PartitionMoveRequest original)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var cut = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, original, Guid.Empty);
            await Assert.That(cut.Header).IsNull();
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.LastIssued).IsNull();
            await Assert.That(cut.SettledStagePages.Length).IsEqualTo(NoOutstandingGrants);
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(NoOutstandingGrants);
        }
    }

    internal static async Task TerminalAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveResult expected)
    {
        await Assert.That(cuts.Count(cut => cut.Header is not null)).IsEqualTo(ControlVoters);
        foreach (var cut in cuts)
        {
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(NoOutstandingGrants);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(NoOutstandingGrants);
            if (cut.Header is not { } header)
            { continue; }
            await Assert.That(header.PendingOriginalPhaseCommandId).IsNull();
            await Assert.That(cut.Pending).IsNull();
            await SqlRf3Protocol.EqualAsync(expected, header.TerminalResult);
            await Assert.That(header.TerminalObservationReceipt).IsNotNull();
            await Assert.That(cut.OutstandingPrincipalGrants).IsEqualTo(NoOutstandingGrants);
        }
    }
}
