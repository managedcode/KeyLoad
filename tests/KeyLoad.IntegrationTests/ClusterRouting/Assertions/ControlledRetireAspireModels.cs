using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using C = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireCorpus;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireModels
{
    private const string VectorSpace = "vector";
    private const string BlobHeadSpace = "blob-head-v1";

    internal static async Task<ControlledRetireAspireSeedState> NativeAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireSeedState state, IReadOnlyDictionary<string, NativeRetireCutWitness> cuts)
    {
        var target = seed.Directory.Owners.Single(owner =>
            owner.Owner.PhysicalShardId == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        var targets = cuts.Values.Where(cut => cut.Identity.Incarnation == target.Incarnation).ToArray();
        await Assert.That(targets.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        var reference = new EntityRef(seed.Partition, C.Collection, C.DocumentId);
        var key = DocumentStorageKeys.RecordKey(reference);
        var recordedAt = state.RecordedAt ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
        foreach (var destination in targets)
        {
            var document = Read<DocumentRecord>(destination, key);
            await SqlRf3Protocol.EqualAsync(new DocumentRecord(reference, C.Revision, C.OriginalJson, new(), recordedAt), document);
            var vector = Read<VectorRecord>(destination,
                KeySpace.Partition(VectorSpace, seed.Partition, C.Collection, C.VectorField, C.DocumentId));
            await SqlRf3Protocol.EqualAsync(new VectorRecord(C.DocumentId, C.VectorField, C.Space,
                [C.UnitVector, C.ZeroVector], C.Revision), vector);
            var blob = Read<KeyLoad.Core.Features.BlobStorage.BlobHead>(destination,
                KeySpace.Partition(BlobHeadSpace, seed.Partition, C.BlobResource, C.BlobId));
            await SqlRf3Protocol.EqualAsync(state.Blob.Expected.Value, blob.Metadata);
        }
        return state;
    }

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireSeedState state, CancellationToken token)
    {
        var reference = new EntityRef(seed.Partition, C.Collection, C.DocumentId);
        var document = new DocumentResult(reference, C.Revision, C.OriginalJson, false, []);
        await SqlRf3Protocol.EqualAsync(document,
            await PublicReadAssertions.DocumentAsync(await seed.Source.GetAsync(reference, token)));
        await SqlRf3Protocol.EqualAsync(document, (await PublicReadAssertions.DocumentAsync(
            await seed.Official.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        var search = new SearchRequest(seed.Partition, C.Collection, VectorField: C.VectorField,
            Vector: [C.UnitVector, C.ZeroVector], Space: C.Space);
        var ranks = new RankedDocument[] { new(document, C.SingleRank) };
        await SqlRf3Protocol.EqualAsync(ranks, await McpCallerAssertions.SdkSuccessAsync(await seed.Source.SearchAsync(search, token)));
        await SqlRf3Protocol.EqualAsync(ranks, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await seed.Official.CallAsync(McpCallerTools.SearchExecute, search, token))).Value);
        var request = new InspectMessageRequest(new(seed.Partition, C.Queue), C.MessageId);
        var message = new MessageInspection(new(C.MessageId, MessageState.Ready, C.Zero, C.Revision, C.Revision, null, null),
            C.CanonicalQueueJson, C.EmptyHeaders);
        await SqlRf3Protocol.EqualAsync(message, await PublicReadAssertions.MessageAsync(await seed.Source.InspectAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(message, (await PublicReadAssertions.MessageAsync(
            await seed.Official.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value);
        await EventsAsync(seed, state, token);
    }

    private static async Task EventsAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireSeedState state, CancellationToken token)
    {
        var source = new EventSourceRef(seed.Partition, C.Topic, EventSourceKind.Topic);
        var request = new ReadEventSourceRequest(source, Limit: C.One);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadEventSourceAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(
            await seed.Official.CallAsync(McpCallerTools.EventsRead, request, token))).Value;
        foreach (var page in new[] { sdk, mcp })
        {
            await SqlRf3Protocol.EqualAsync(source, page.Source);
            await SqlRf3Protocol.EqualAsync(new EventSourceHead(C.Revision, C.Revision, source.Generation), page.Head);
            await SqlRf3Protocol.EqualAsync(new SourceEventRecord[] { new(source, C.Revision, C.Revision,
                new(C.EventId, C.EventType, C.EventJson, C.EmptyHeaders), state.RecordedAt
                    ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing)) }, page.Events.ToArray());
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.CutPosition).IsGreaterThan(C.Revision);
            await Assert.That(string.IsNullOrWhiteSpace(page.Cursor)).IsFalse();
        }
        var nextSdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadEventSourceAsync(request with
        {
            Cursor = sdk.Cursor
        }, token));
        var nextMcp = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await seed.Official.CallAsync(
            McpCallerTools.EventsRead, request with
            {
                Cursor = mcp.Cursor
            }, token))).Value;
        await Assert.That(nextSdk.CutPosition).IsEqualTo(sdk.CutPosition);
        await Assert.That(nextMcp.CutPosition).IsEqualTo(mcp.CutPosition);
        foreach (var page in new[] { nextSdk, nextMcp })
        {
            await SqlRf3Protocol.EqualAsync(source, page.Source);
            await SqlRf3Protocol.EqualAsync(new EventSourceHead(C.Revision, C.Revision, source.Generation), page.Head);
            await Assert.That(page.Events).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.CutPosition).IsGreaterThan(C.Revision);
        }
    }

    private static T Read<T>(NativeRetireCutWitness source, byte[] key)
        => NativeSerialization.Deserialize<T>(source.Rows.Single(row => row.Key.Span.SequenceEqual(key)).Value.Span);
}
