using KeyLoad.Client;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using C = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireCorpus;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireBlob
{
    private static readonly Guid ConfigureId = Guid.Parse("a3aa5b55-c605-4c3b-9e49-871b876ef608");
    private static readonly Guid BeginId = Guid.Parse("28175434-9a57-434e-b201-b376c9206f2d");
    private static readonly Guid PartId = Guid.Parse("5738caef-a9e4-4623-ad93-d91e3838aff6");

    internal static async Task<ControlledRetireAspireBlobState> CreateAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var definition = new ResourceDefinition(C.BlobResource, ResourceKind.BlobStore, seed.Partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigureResourceAsync(ConfigureId, new(seed.Partition.TenantId, seed.Partition.DatabaseId, definition), token)));
        var blob = new BlobRef(seed.Partition, C.BlobResource, C.BlobId);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(
            new(BeginId, blob, C.UploadId, C.BlobLength, C.Zero), token));
        await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(
            new(PartId, blob, C.UploadId, C.Zero, C.BlobBytes, C.BlobHash), token));
        var initial = BlobIntegrity.InitialHash(seed.OriginalPlacement.Incarnation, blob, C.UploadId, C.BlobLength);
        await Assert.That(begun.Value.IntegrityHash).IsEqualTo(initial);
        var hash = BlobIntegrity.NextHash(initial, C.Zero, C.BlobLength, C.BlobHash);
        var request = new CompleteBlobUploadRequest(C.CompleteId, blob, C.UploadId, hash);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CompleteBlobUploadAsync(request, token));
        var expected = new BlobMetadata(blob, C.Revision, C.UploadId, C.BlobLength, C.One, hash, new(), actual.Value.UpdatedAt);
        await SqlRf3Protocol.EqualAsync(expected, actual.Value);
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(C.CompleteId);
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(actual.Receipt.Token.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(actual.Receipt.Token.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(C.Revision);
        await SqlRf3Protocol.EqualAsync<MutationReceipt[]>([], actual.Receipt.Mutations.ToArray());
        var state = new ControlledRetireAspireBlobState(request, actual);
        await ReplayAsync(seed, state, token);
        return state;
    }

    internal static async Task<ControlledRetireAspireBlobState> NativeAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireBlobState state, IReadOnlyDictionary<string, NativeRetireCutWitness> cuts)
    {
        var sources = cuts.Values.Where(cut => cut.Identity.Incarnation == seed.OriginalPlacement.Incarnation).ToArray();
        await Assert.That(sources.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var source in sources)
        {
            var entry = source.ReceiptEntry ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
            await Assert.That(source.CommittedIndex).IsGreaterThanOrEqualTo(entry.Index);
            var operation = entry.Operation ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.CompleteBlobUpload);
            await Assert.That(operation.Id).IsEqualTo(state.Request.CommandId);
            await Assert.That(operation.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
            await SqlRf3Protocol.EqualAsync(state.Request, NativeCommandPayload.Read<CompleteBlobUploadRequest>(operation));
            var initial = BlobIntegrity.InitialHash(source.Identity.Incarnation, state.Request.Blob, C.UploadId, C.BlobLength);
            var integrity = BlobIntegrity.NextHash(initial, C.Zero, C.BlobLength, C.BlobHash);
            await Assert.That(state.Request.ExpectedIntegrityHash).IsEqualTo(integrity);
            var metadata = new BlobMetadata(state.Request.Blob, C.Revision, C.UploadId, C.BlobLength, C.One,
                integrity, new(), operation.EvaluatedAt, false);
            var receipt = new CommitReceipt(state.Request.CommandId,
                new(source.Identity.Incarnation, seed.Partition.AtomicPartitionId, entry.Index,
                    seed.OriginalPlacement.PlacementEpoch), [], DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(new BlobCommitResult<BlobMetadata>(receipt, metadata), state.Expected);
        }
        return state;
    }

    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireBlobState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Expected, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CompleteBlobUploadAsync(state.Request, token)));
        await SqlRf3Protocol.EqualAsync(state.Expected, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await seed.Official.CallAsync(BlobToolNames.CompleteUpload, state.Request, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Expected.Value, await PublicReadAssertions.MetadataAsync(
            await seed.Source.GetBlobMetadataAsync(new(state.Request.Blob), token)));
        await SqlRf3Protocol.EqualAsync(state.Expected.Value, (await PublicReadAssertions.MetadataAsync(
            await seed.Official.CallAsync(BlobToolNames.Metadata, new BlobMetadataRequest(state.Request.Blob), token))).Value);
        foreach (var (offset, bytes) in new[] { (C.Zero, C.BlobBytes), (C.One, C.PartialBytes) })
        {
            var request = new BlobReadRequest(state.Request.Blob, C.Revision, offset, bytes.Length);
            var expected = new BlobReadResult(state.Expected.Value, offset, bytes);
            await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadBlobRangeAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await seed.Official.CallAsync(BlobToolNames.ReadRange, request, token))).Value);
        }
    }
}
