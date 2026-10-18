using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnOriginalReplay
{
    private const int EmptyPayload = 0;

    internal static async Task RequireAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest request, AnnMaintenanceResult expected, CancellationToken token)
    {
        var canonical = global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var nativeRoot = Directory.Exists(Path.Combine(fixture.Database.Directory,
            global::KeyLoad.Server.Features.Search.NativeAnnProtocol.RootDirectory));
        var native = nativeRoot ? NativeAnnFixtureFileSnapshot.Capture(fixture.Database.Directory) : [];
        var actual = await RequestCqrsAnnInvocation.RunAsync(fixture, request, token, retainedReplay: true);
        await Assert.That(NativeSerialization.Serialize(actual.Result).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await UnchangedAsync(fixture, canonical, cut, native, nativeRoot);
    }

    internal static Task RefuseUnsettledAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, CancellationToken token)
        => RefuseAsync(fixture, original with
        {
            CommandId = Guid.NewGuid()
        }, ErrorCode.RecoveryRequired, token);

    internal static Task RefusePriorAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, CancellationToken token)
        => RefuseAsync(fixture, original, ErrorCode.HistoryUnavailable, token);

    internal static Task RefuseChangedAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, CancellationToken token)
        => RefuseAsync(fixture, original with
        {
            Mode = original.Mode == AnnMaintenanceMode.Build
            ? AnnMaintenanceMode.Restore : AnnMaintenanceMode.Build
        }, ErrorCode.Conflict, token);

    private static async Task RefuseAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, ErrorCode expected, CancellationToken token)
    {
        var canonical = global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var journal = fixture.Database.ReadCanonicalCut();
        var nativeRoot = Directory.Exists(Path.Combine(fixture.Database.Directory,
            global::KeyLoad.Server.Features.Search.NativeAnnProtocol.RootDirectory));
        var native = nativeRoot ? NativeAnnFixtureFileSnapshot.Capture(fixture.Database.Directory) : [];
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, original.CommandId, NativeSerialization.Serialize(original));
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        var connection = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Services, principal,
            id, original.CommandId, token, connectionId: fixture.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => connection.ExecuteStreamAsync(signed, execution),
            fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, fixture.RoutingOptions,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        await Assert.That(reply.Error).IsEqualTo(expected);
        await Assert.That(reply.Payload.Length).IsEqualTo(EmptyPayload);
        await Assert.That(fixture.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(journal.AppliedPosition);
        await Assert.That(fixture.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(journal.AppliedPosition);
        await UnchangedAsync(fixture, canonical, cut, native, nativeRoot);
    }

    private static async Task UnchangedAsync(RequestCqrsHostedRuntimeFixture fixture, string[] expected,
        long cut, (string Path, string Bytes)[] files, bool nativeRoot)
    {
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store).SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(fixture.Database.Directory,
            global::KeyLoad.Server.Features.Search.NativeAnnProtocol.RootDirectory))).IsEqualTo(nativeRoot);
        if (nativeRoot)
        {
            await Assert.That(NativeAnnFixtureFileSnapshot.Capture(fixture.Database.Directory))
            .IsEquivalentTo(files, CollectionOrdering.Matching);
        }
    }
}
