using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobActivePolicyPublicOperations
{
    private const string PartHash = "98f608e5aa56c97d8cf793e433cb3e2afd1bc5e6fa051e968a851c352b4406a0";
    internal static async Task BeginAsync(RequestCqrsRf3Callers creator, BlobActivePolicyPublicState state, CancellationToken token)
    {
        state.Begun = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.BeginBlobUploadAsync(state.Begin, token));
        var actual = state.Begun.Value;
        state.Current = new(state.Blob, state.Begin.UploadId, 4L, 0L, 0, 0L,
            actual.ExpiresAt, BlobUploadStatus.Active, actual.IntegrityHash);
        await SqlRf3Protocol.EqualAsync(state.Current, actual);
        await BlobActivePolicyPublicAssertions.ReceiptAsync(state, state.Begin.CommandId, state.Begun.Receipt);
        var hash = Chain(actual.IntegrityHash);
        state.DeniedPart = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId, 0, state.Bytes, PartHash);
        state.DeniedComplete = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId, hash);
        state.DeniedAbort = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId);
    }

    private static string Chain(string previous)
    {
        Span<byte> frame = stackalloc byte[72];
        Convert.FromHexString(previous).CopyTo(frame);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame[32..], 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame[36..], 4);
        Convert.FromHexString(PartHash).CopyTo(frame[40..]);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(frame));
    }

    internal static async Task PublishAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, BlobActivePolicyPublicState state, CancellationToken token)
    {
        state.Part = state.DeniedPart with { CommandId = Guid.NewGuid() };
        state.Written = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await creator.Mcp.CallAsync("keyload_blobs_write_part", state.Part, token))).Value;
        state.Current = state.Begun.Value with { NextOrdinal = 1, StoredBytes = 4L, IntegrityHash = state.DeniedComplete.ExpectedIntegrityHash };
        await SqlRf3Protocol.EqualAsync(state.Current, state.Written.Value);
        await BlobActivePolicyPublicAssertions.ReceiptAsync(state, state.Part.CommandId, state.Written.Receipt);
        await BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token);
        state.Complete = state.DeniedComplete with { CommandId = Guid.NewGuid() };
        state.Completed = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.CompleteBlobUploadAsync(state.Complete, token));
        var expected = new BlobMetadata(state.Blob, 1L, state.Begin.UploadId, 4L, 1,
            state.DeniedComplete.ExpectedIntegrityHash, new RowAccess(), state.Completed.Value.UpdatedAt);
        await SqlRf3Protocol.EqualAsync(expected, state.Completed.Value);
        state.Current = state.Current with { Status = BlobUploadStatus.Complete };
        await BlobActivePolicyPublicAssertions.ReceiptAsync(state, state.Complete.CommandId, state.Completed.Receipt);
        await BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, BlobActivePolicyPublicState state, CancellationToken token)
    {
        Task Observe() => BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(creator, "keyload_blobs_write_part", state.Part,
            state.Written, ct => creator.Sdk.WriteBlobPartAsync(state.Part, ct), Observe, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(creator, "keyload_blobs_complete_upload", state.Complete,
            state.Completed!, ct => creator.Sdk.CompleteBlobUploadAsync(state.Complete, ct), Observe, token);
    }
}
