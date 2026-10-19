using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingPublicContinuation
{
    private const string PutKind = "putDocument";

    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient administrator,
        GraphIncomingClusterSeed seed, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, seed.Reader.Secret,
            async callers =>
            {
                await GraphIncomingPublicRoutes.HealthyAsync(callers, seed, false, token);
                await GraphIncomingPublicPolicy.RunAsync(fixture, administrator, callers, seed, token);
                return true;
            }, token);
        var command = Write(seed, GraphIncomingPublicPolicy.InitialRevision);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token));
        await LiteralReceiptAsync(command, receipt, GraphIncomingPublicPolicy.HealthyRevision);
        await GraphIncomingPublicImages.LiteralDocumentsAsync(administrator, seed, GraphIncomingPublicPolicy.HealthyRevision, token);
        var model = await GraphIncomingPublicImages.CaptureAsync(administrator, seed, token);
        await ReplayAsync(fixture, command, receipt, token);
        await GraphIncomingPublicImages.SameAsync(model, await GraphIncomingPublicImages.CaptureAsync(administrator, seed, token));
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var fresh = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await GraphIncomingPublicImages.SameAsync(model, await GraphIncomingPublicImages.CaptureAsync(fresh, seed, token));
        await ReplayAsync(fixture, command, receipt, token);
        await GraphIncomingPublicImages.SameAsync(model, await GraphIncomingPublicImages.CaptureAsync(fresh, seed, token));
        var following = Write(seed, GraphIncomingPublicPolicy.HealthyRevision);
        await LiteralReceiptAsync(following, await McpCallerAssertions.SdkSuccessAsync(
            await fresh.CommitAsync(following, token)), GraphIncomingPublicPolicy.FollowingRevision);
        await GraphIncomingPublicImages.LiteralDocumentsAsync(fresh, seed, GraphIncomingPublicPolicy.FollowingRevision, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, seed.Reader.Secret,
            async callers => { await GraphIncomingPublicRoutes.HealthyAsync(callers, seed, false, token); return true; }, token);
    }

    private static CommandRequest Write(GraphIncomingClusterSeed seed, long expectedRevision)
        => new(Guid.NewGuid(), seed.TargetPartition, [new PutDocument(GraphIncomingClusterScenario.Collection,
            seed.LocalSource.Id, GraphIncomingClusterScenario.EmptyJson, ExpectedRevision: expectedRevision)]);

    private static async Task ReplayAsync(ClusterFixture fixture,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers =>
            {
                await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
                    await callers.Sdk.CommitAsync(command, token)));
                await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
                var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
                await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
                await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
                return true;
            }, token);
    }

    private static async Task LiteralReceiptAsync(CommandRequest command, CommitReceipt receipt, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(GraphIncomingPublicPolicy.EmptyPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(GraphIncomingPublicPolicy.Version);
        var mutation = (PutDocument)command.Mutations.Single();
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlClusterProtocol.EqualAsync(
            new MutationReceipt(PutKind, mutation.Collection, mutation.Id, revision), receipt.Mutations.Single());
    }
}
