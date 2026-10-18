using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryHost.Features.BlobStorage;
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
        await Assert.That(store.Identity.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
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
            var original = NativeSerialization.Deserialize<Command<DatabaseCommand>>(proof.Operations[i]);
            var result = database.Apply(original);
            await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(proof.Results[i])).IsTrue();
        }
        await Assert.That(Image((ZoneTreeStore)database.Store)).IsEqualTo(image);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task DeniedOutcomesAsync(DatabaseEngine database, BlobCommitCutSeedProof proof,
        Command<DatabaseCommand> selected)
    {
        var store = (ZoneTreeStore)database.Store;
        var image = Image(store);
        var position = store.Position;
        var originals = proof.Operations.Select(bytes => NativeSerialization.Deserialize<Command<DatabaseCommand>>(bytes)).Append(selected);
        foreach (var original in originals)
        {
            var payload = original.Value!.Kind switch
            {
                OperationKind.ConfigurePrincipal => NativeSerialization.Serialize(NativeSerialization.Deserialize<ConfigurePrincipalRequest>((original).Value!.Input.Span)),
                OperationKind.ConfigureResource => NativeSerialization.Serialize(NativeSerialization.Deserialize<ConfigureResourceRequest>((original).Value!.Input.Span)),
                OperationKind.BeginBlobUpload => NativeSerialization.Serialize(NativeSerialization.Deserialize<BeginBlobUploadRequest>((original).Value!.Input.Span)),
                OperationKind.WriteBlobPart => NativeSerialization.Serialize(NativeSerialization.Deserialize<WriteBlobPartRequest>((original).Value!.Input.Span)),
                OperationKind.CompleteBlobUpload => NativeSerialization.Serialize(NativeSerialization.Deserialize<CompleteBlobUploadRequest>((original).Value!.Input.Span)),
                OperationKind.DeleteBlob => NativeSerialization.Serialize(NativeSerialization.Deserialize<DeleteBlobRequest>((original).Value!.Input.Span)),
                _ => throw new InvalidOperationException("The original blob denial corpus contains an unsupported command.")
            };
            var denied = database.CreateCommand(original.Value!.Kind, original.CommandId, BlobCommitCutProtocol.DeniedPrincipal,
                database.EvaluationClock.GetUtcNow(), payload);
            var result = database.ResolveOutcome(denied);
            await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(result.Value).IsNull();
            await Assert.That(Image(store)).IsEqualTo(image);
            await Assert.That(store.Position).IsEqualTo(position);
        }
    }

    internal static async Task<Command<DatabaseCommand>> OriginalOperationAsync(string root, CancellationToken cancellationToken)
        => NativeSerialization.Deserialize<Command<DatabaseCommand>>(await BlobCommitCutSeedProofReader.ReadAsync(root,
            BlobCommitCutProtocol.OperationFile, cancellationToken));

    internal static async Task<Result<object>> ReturnedAsync(string root, CancellationToken cancellationToken)
        => NativeSerialization.Deserialize<Result<object>>(await BlobCommitCutSeedProofReader.ReadAsync(root,
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
