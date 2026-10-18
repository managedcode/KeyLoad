using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCommitCutNativeProof
{
    internal static ZoneTreeStore Open(string root) => new(new(root),
        CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());

    internal static DatabaseEngine Database(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(),
        CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(),
        CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());

    internal static string Image(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], BlobCommitCutProtocol.MaximumRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The bounded recovered blob image is incomplete.");
        }
        return Convert.ToHexString(JsonDefaults.Serialize(page.Records));
    });

    internal static async Task AssertAuthorityAsync(ZoneTreeStore store, DatabaseEngine database, BlobCommitCutSeedProof proof)
    {
        await Assert.That(store.Identity.NodeId).IsEqualTo(proof.NodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(proof.Incarnation);
        await Assert.That(store.Identity.ReadGeneration).IsEqualTo(proof.ReadGeneration);
        var actualCatalog = database.ReadPhysicalShardCatalog(BlobCommitCutProtocol.Principal);
        await Assert.That(actualCatalog.Version).IsEqualTo(1);
        await Assert.That(actualCatalog.Revision).IsEqualTo(1L);
        await Assert.That(actualCatalog.DefaultShard.PhysicalShardId).IsEqualTo(Guid.Parse("778899aa-bbcc-ddee-ff00-112233445566"));
        await Assert.That(actualCatalog.DefaultShard.Incarnation).IsEqualTo(proof.Incarnation);
        await Assert.That(actualCatalog.DefaultShard.PlacementEpoch).IsEqualTo(1L);
        string[] voters = ["crash-a", "crash-b", "crash-c"];
        await Assert.That(actualCatalog.DefaultShard.VoterIds.SequenceEqual(voters, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Identity.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        var catalog = NativeSerialization.Serialize(actualCatalog);
        await Assert.That(catalog.SequenceEqual(proof.Catalog)).IsTrue();
        var principal = store.Read(view => view.ReadOwnedValue(KeySpace.Principal(BlobCommitCutProtocol.Principal)));
        var denied = store.Read(view => view.ReadOwnedValue(KeySpace.Principal(BlobCommitCutProtocol.DeniedPrincipal)));
        await Assert.That(principal is not null && principal.SequenceEqual(proof.PrincipalBytes)).IsTrue();
        await Assert.That(denied is not null && denied.SequenceEqual(proof.DeniedPrincipalBytes)).IsTrue();
    }

    internal static async Task ReplaySeedsAsync(DatabaseEngine database, BlobCommitCutSeedProof proof)
    {
        await Assert.That(proof.Operations.Length).IsEqualTo(proof.Results.Length);
        var image = Image((ZoneTreeStore)database.Store);
        var position = database.Store.Position;
        for (var i = 0; i < proof.Operations.Length; i++)
        {
            var original = NativeSerialization.Deserialize<ReplicatedOperation>(proof.Operations[i]);
            var result = database.Apply(original);
            await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(proof.Results[i])).IsTrue();
        }
        await Assert.That(Image((ZoneTreeStore)database.Store)).IsEqualTo(image);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task DeniedOutcomesAsync(DatabaseEngine database, BlobCommitCutSeedProof proof,
        ReplicatedOperation selected)
    {
        var store = (ZoneTreeStore)database.Store;
        var image = Image(store);
        var position = store.Position;
        var originals = proof.Operations.Select(bytes => NativeSerialization.Deserialize<ReplicatedOperation>(bytes)).Append(selected);
        foreach (var original in originals)
        {
            var payload = original.Kind switch
            {
                OperationKind.ConfigurePrincipal => NativeSerialization.Serialize(NativeCommandPayload.Read<ConfigurePrincipalRequest>(original)),
                OperationKind.ConfigureResource => NativeSerialization.Serialize(NativeCommandPayload.Read<ConfigureResourceRequest>(original)),
                OperationKind.BeginBlobUpload => NativeSerialization.Serialize(NativeCommandPayload.Read<BeginBlobUploadRequest>(original)),
                OperationKind.WriteBlobPart => NativeSerialization.Serialize(NativeCommandPayload.Read<WriteBlobPartRequest>(original)),
                OperationKind.CompleteBlobUpload => NativeSerialization.Serialize(NativeCommandPayload.Read<CompleteBlobUploadRequest>(original)),
                OperationKind.DeleteBlob => NativeSerialization.Serialize(NativeCommandPayload.Read<DeleteBlobRequest>(original)),
                _ => throw new InvalidOperationException("The original blob denial corpus contains an unsupported command.")
            };
            var denied = database.CreateNativeOperation(original.Kind, original.Id, BlobCommitCutProtocol.DeniedPrincipal,
                database.EvaluationClock.GetUtcNow(), payload);
            var result = database.ResolveOutcome(denied);
            await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(result.Json).IsNull();
            await Assert.That(result.NativeValue).IsNull();
            await Assert.That(Image(store)).IsEqualTo(image);
            await Assert.That(store.Position).IsEqualTo(position);
        }
    }

    internal static async Task<ReplicatedOperation> OriginalOperationAsync(string root, CancellationToken cancellationToken)
        => NativeSerialization.Deserialize<ReplicatedOperation>(await BlobCommitCutSeedProofReader.ReadAsync(root,
            BlobCommitCutProtocol.OperationFile, cancellationToken));

    internal static async Task<OperationResult> ReturnedAsync(string root, CancellationToken cancellationToken)
        => NativeSerialization.Deserialize<OperationResult>(await BlobCommitCutSeedProofReader.ReadAsync(root,
            BlobCommitCutProtocol.ReturnedFile, cancellationToken));

    internal static async Task AssertFaultAsync(string root, CancellationToken cancellationToken)
    {
        var bytes = await BlobCommitCutSeedProofReader.ReadAsync(root, BlobCommitCutProtocol.FaultFile, cancellationToken);
        var fault = JsonSerializer.Deserialize<BlobCommitCutObservedFault>(bytes, JsonDefaults.Options)
            ?? throw new InvalidOperationException("The actual native blob fault observation is absent.");
        var seed = await BlobCommitCutSeedProofReader.LoadAsync(root, cancellationToken);
        await Assert.That(fault.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(fault.Detail).IsEqualTo(BlobCommitCutProtocol.UnknownDetail);
        await Assert.That(fault.PoisonCode).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(fault.PoisonDetail).IsEqualTo(BlobCommitCutProtocol.PoisonDetail);
        await Assert.That(fault.Stage).IsEqualTo(CommitStage.JournalFlushed);
        await Assert.That(fault.Position).IsEqualTo(checked(seed.Position + 1));
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(File.Exists(Path.Combine(root, BlobCommitCutProtocol.ReturnedFile))).IsFalse();
    }
}
