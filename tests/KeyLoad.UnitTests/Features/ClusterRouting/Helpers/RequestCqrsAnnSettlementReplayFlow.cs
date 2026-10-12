using KeyLoad.Orleans;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnSettlementReplayFlow
{
    private const string AlteredProofSuffix = "-changed";
    private const int EmptyPayload = 0;

    internal static async Task RunAsync(RequestCqrsClusterFixture fixture,
        AnnMaintenanceRequest original, AnnMaintenanceResult result, CancellationToken token)
    {
        var root = Path.Combine(fixture.Database.Directory, NativeAnnProtocol.RootDirectory, NativeAnnProtocol.RootReceipt);
        var entry = NativeAnnReceiptFiles.Read<NativeAnnRootReceipt>(root, new NativeAnnExecutionOptions()).Value
            .OriginalOperations.Single(value => value.Key == new NativeAnnOwnedKey(original.Consumer, original.IndexGeneration));
        var intent = entry.InitialSettlementIntent ?? throw new InvalidOperationException();
        var expected = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, original.Consumer.Partition,
            RequestCqrsAnnTestData.Principal, intent.CommandId)?.Get<ProjectionBatchResult>() ?? throw new InvalidOperationException();
        await ReplayAsync(fixture, intent, expected, token);
        await RefuseAsync(fixture, intent with { InitialAnnMaintenanceSettlement = null }, token);
        await RefuseAsync(fixture, intent with
        { InitialAnnMaintenanceSettlement = intent.InitialAnnMaintenanceSettlement + AlteredProofSuffix }, token);
        await ReplayAsync(fixture, intent, expected, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, original, result, token);
    }

    private static async Task ReplayAsync(RequestCqrsClusterFixture fixture,
        CommitProjectionBatchRequest request, ProjectionBatchResult expected, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var files = NativeAnnFixtureFileSnapshot.Capture(fixture.Database.Directory);
        var reply = await InvokeAsync(fixture, request, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as ProjectionBatchResult
            ?? throw new InvalidOperationException();
        await RequestCqrsAnnAssertions.EqualAsync(expected, actual);
        await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await UnchangedAsync(fixture, image, cut, files);
    }

    private static async Task RefuseAsync(RequestCqrsClusterFixture fixture,
        CommitProjectionBatchRequest changed, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var files = NativeAnnFixtureFileSnapshot.Capture(fixture.Database.Directory);
        var reply = await InvokeAsync(fixture, changed, token);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(reply.Payload.Length).IsEqualTo(EmptyPayload);
        await UnchangedAsync(fixture, image, cut, files);
    }

    private static async Task<GrainOperationReply> InvokeAsync(RequestCqrsClusterFixture fixture,
        CommitProjectionBatchRequest request, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.CommitProjectionBatch, request.CommandId, NativeSerialization.Serialize(request));
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        var connection = fixture.Cluster.Client.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
            id, request.CommandId, token, connectionId: fixture.ConnectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            execution => connection.ExecuteStreamAsync(signed, execution),
            fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, fixture.RoutingOptions, token);
    }

    private static async Task UnchangedAsync(RequestCqrsClusterFixture fixture,
        string[] image, long cut, (string Path, string Bytes)[] files)
    {
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(NativeAnnFixtureFileSnapshot.Capture(fixture.Database.Directory))
            .IsEquivalentTo(files, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
