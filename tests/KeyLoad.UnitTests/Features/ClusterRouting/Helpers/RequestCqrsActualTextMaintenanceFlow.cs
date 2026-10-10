using System.Runtime.CompilerServices;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsActualTextMaintenanceFlow
{
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";
    private const long OriginalRevision = 1;
    private const int TrackedRecords = 2;
    private const int EmptyEffects = 0;

    internal static Task RunAsync(RequestCqrsClusterFixture fixture, TextIndexMaintenanceMode mode,
        CancellationToken token) => RunAsync(fixture, mode, null, token);

    internal static async Task RunAsync(RequestCqrsClusterFixture fixture, TextIndexMaintenanceMode mode,
        TextIndexMaintenanceRequest? prepared, CancellationToken token)
    {
        if (prepared is null)
        { fixture.Database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection); }
        var request = prepared ?? await NativeTextMaintenanceRequestFixture.CreateAsync(fixture.Database, token);
        _ = await NativeTextMaintenanceSeed.CommitAsync(fixture.Database, token);
        var built = await ExecuteAsync(fixture, request, token);
        await VerifyResultAsync(fixture, request, built);
        await RequestCqrsActualTextReplayFlow.ReplayAsync(fixture, request, built, token);
        await RequestCqrsActualTextAssertions.VerifyAsync(fixture, request, changed: false, token);
        if (mode == TextIndexMaintenanceMode.Restore)
        {
            var id = Guid.NewGuid();
            var write = new CommandRequest(id, fixture.Database.Partition,
                [new PutDocument(request.Collection, NativeTextBilingualAudit.UkrainianId,
                    ChangedJson, ExpectedRevision: OriginalRevision),
                 new DeleteDocument(request.Collection, NativeTextBilingualAudit.EnglishId,
                    ExpectedRevision: OriginalRevision)]);
            _ = await NativeTextMaintenanceCommit.ExecuteAsync<CommitReceipt>(fixture.Database,
                OperationKind.Batch, write, id, token);
            var restore = request with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
            var restored = await ExecuteAsync(fixture, restore, token);
            await VerifyResultAsync(fixture, restore, restored);
            await RequestCqrsActualTextReplayFlow.ReplayAsync(fixture, restore, restored, token);
            var restoredSource = restored.Source ?? throw new InvalidOperationException();
            var builtSource = built.Source ?? throw new InvalidOperationException();
            await Assert.That(restoredSource.ThroughSequence).IsGreaterThan(builtSource.ThroughSequence);
            await Assert.That(restored.IndexSha256).IsNotEqualTo(built.IndexSha256);
            await RequestCqrsActualTextAssertions.VerifyAsync(fixture, restore, changed: true, token);
            await RequestCqrsActualTextReplayFlow.PriorParentAsync(fixture, restore, token);
        }
        await RequestCqrsActualTextReplayFlow.ReleaseAsync(fixture, request, token);
    }

    internal static Task<TextIndexMaintenanceResult> ExecuteAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, CancellationToken token) => ExecuteAsync(fixture, request, true, token);

    internal static async Task<TextIndexMaintenanceResult> ExecuteAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, bool expectNativeWork, CancellationToken token)
    {
        var reply = await InvokeAsync(fixture, request, expectNativeWork, token);
        await Assert.That(reply.Error).IsNull();
        return NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as TextIndexMaintenanceResult
            ?? throw new InvalidOperationException();
    }

    internal static async Task<GrainOperationReply> InvokeAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, bool expectNativeWork, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, NativeTextMaintenanceTestValues.Principal,
            OperationKind.MaintainTextIndex, request.CommandId, NativeSerialization.Serialize(request));
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            NativeTextMaintenanceTestValues.Principal, fixture.Clock.GetUtcNow()));
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var connection = fixture.Cluster.Client.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
            id, request.CommandId, token, connectionId: fixture.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => ObserveAsync(connection.ExecuteStreamAsync(signed, execution), frames, execution),
            fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, fixture.RoutingOptions,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        if (reply.Error is not null)
        {
            var phases = frames.Where(frame => frame.Kind == CqrsStreamChunkKind.Progress)
                .Select(frame => frame.ProgressResult!.Value.Value!.TextPhase);
            await Console.Error.WriteLineAsync($"KL029_ACTUAL_BOUNDARY mode={request.Mode} error={reply.Error} phases={string.Join(',', phases)} detail={reply.SafeDetail}").ConfigureAwait(false);
        }
        if (reply.Error == ErrorCode.Corruption)
        {
            var failures = new List<Exception> { Errors.Fail(ErrorCode.Corruption,
                reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest) };
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => RequestCqrsNativeTextCheckpointObservation.RecordAsync(fixture, request, token), failures);
            if (failures.Count > 1)
            { KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures); }
        }
        await RequestCqrsActualTextReplayFlow.VerifyFramesAsync(frames, request.Mode,
            expectNativeWork, reply.Error);
        return reply;
    }

    private static async Task VerifyResultAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, TextIndexMaintenanceResult result)
    {
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(result.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(result.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(result.Phase).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(result.TrackedRecords).IsEqualTo(TrackedRecords);
        var source = result.Source ?? throw new InvalidOperationException();
        await Assert.That(source.NodeId).IsEqualTo(fixture.Database.Store.Identity.NodeId);
        await Assert.That(source.Incarnation).IsEqualTo(fixture.Database.Store.Identity.Incarnation);
        var checkpoint = result.Checkpoint ?? throw new InvalidOperationException();
        await Assert.That(checkpoint.Receipt.Mutations.Length).IsEqualTo(EmptyEffects);
        await Assert.That(checkpoint.Receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(result.ReleasedConsumer).IsNull();
    }

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ObserveAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> original,
        List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var frame in original.WithCancellation(token))
        { frames.Add(frame); yield return frame; }
    }
}
