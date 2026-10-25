using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class RemoteTransferRepairClusterTrial
{
    internal static async Task RunAsync(QueueTransferRepairStage stage)
    {
        var subject = RemoteTransferCoordinatorClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new RemoteTransferCoordinatorFixtureSelection(subject,
                RepairCeiling: RemoteTransferRepairClusterProtocol.Ceiling));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var original = await RemoteTransferRepairClusterPreparation.CreateAsync(fixture, subject, stage, deadline.Token);
                await QueueProducerClusterCold.RestartAsync(fixture, deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed, async (sdk, mcp) =>
                {
                    await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, original.Seed, original.Intent, deadline.Token);
                    await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, original.Seed, original.AcceptedProof, deadline.Token);
                    await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(original.Failed, deadline.Token), ErrorCode.PermissionDenied);
                }, deadline.Token);
                var repaired = original.Seed.Identity.Principal with
                {
                    PolicyEpoch = checked(original.Seed.Identity.Principal.PolicyEpoch + RemoteTransferRepairClusterProtocol.RepairedEpochOffset)
                };
                await MessagingClusterIdentity.UpdateAsync(fixture, repaired, deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed,
                    (sdk, mcp) => RemoteTransferRepairClusterHealthy.ProveAsync(sdk, mcp, original, acked: false, deadline.Token), deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed,
                    (sdk, mcp) => RemoteTransferRepairClusterAck.ExecuteAsync(sdk, mcp, original, deadline.Token), deadline.Token);
                await QueueProducerClusterCold.RestartAsync(fixture, deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed,
                    (sdk, mcp) => RemoteTransferRepairClusterHealthy.ProveAsync(sdk, mcp, original, acked: true, deadline.Token), deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
