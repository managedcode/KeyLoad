using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRepairRf3Trial
{
    internal static async Task RunAsync(QueueTransferRepairStage stage)
    {
        var subject = RemoteTransferCoordinatorRf3Protocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new RemoteTransferCoordinatorFixtureSelection(subject,
                RepairCeiling: RemoteTransferRepairRf3Protocol.Ceiling));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var original = await RemoteTransferRepairRf3Preparation.CreateAsync(fixture, subject, stage, deadline.Token);
                await QueueProducerRf3Cold.RestartAsync(fixture, deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed, async (sdk, mcp) =>
                {
                    await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, original.Seed, original.Intent, deadline.Token);
                    await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, original.Seed, original.AcceptedProof, deadline.Token);
                    await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(original.Failed, deadline.Token), ErrorCode.PermissionDenied);
                }, deadline.Token);
                var repaired = original.Seed.Identity.Principal with
                { PolicyEpoch = checked(original.Seed.Identity.Principal.PolicyEpoch + RemoteTransferRepairRf3Protocol.RepairedGeneration) };
                await MessagingRf3Identity.UpdateAsync(fixture, repaired, deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed,
                    (sdk, mcp) => RemoteTransferRepairRf3Healthy.ProveAsync(sdk, mcp, original, acked: false, deadline.Token), deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed,
                    (sdk, mcp) => RemoteTransferRepairRf3Ack.ExecuteAsync(sdk, mcp, original, deadline.Token), deadline.Token);
                await QueueProducerRf3Cold.RestartAsync(fixture, deadline.Token);
                await RemoteTransferColdCallers.WithAsync(fixture, original.Seed,
                    (sdk, mcp) => RemoteTransferRepairRf3Healthy.ProveAsync(sdk, mcp, original, acked: true, deadline.Token), deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
