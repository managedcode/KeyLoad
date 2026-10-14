using System.Security.Cryptography;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaAppliedOutcomeWholeFlow
{
    private const string Resource = "applied-refusal-blobs";
    private const string CreatorDenial = "The persisted principal cannot access this blob.";
    private const string MissingLifetime = "The selected upload lifetime is unavailable.";
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";

    internal static Task RunAsync(int scenario) => ReplicaSubmissionOutcomeFlow.RunAsync(async owner =>
    {
        var configured = Configure(owner);
        var blob = new BlobRef(owner.Database.Partition, Resource, "original");
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, 1, 0);
        var begun = owner.Database.Submit(OperationKind.BeginBlobUpload, begin,
            principal: scenario == 2 ? ReplicaSubmissionOutcomeFlow.Writer : "root", id: begin.CommandId);
        await Assert.That(begun.Get<BlobCommitResult<BlobUploadInfo>>().Receipt.CommandId).IsEqualTo(begin.CommandId);
        byte[] bytes = [42];
        var request = new WriteBlobPartRequest(Guid.NewGuid(), blob,
            scenario == 1 ? Guid.NewGuid() : upload, 0, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var operation = Issue(owner, OperationKind.WriteBlobPart, request, request.CommandId, ReplicaSubmissionOutcomeFlow.Writer);
        var original = owner.Submit(operation);
        if (scenario == 2)
        {
            await Assert.That(original.Error).IsNull();
            var changed = configured with
            {
                PolicyEpoch = checked(configured.PolicyEpoch + 1)
            };
            owner.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(changed));
        }
        else
        {
            await Assert.That(original.Error).IsEqualTo(scenario == 0 ? ErrorCode.PermissionDenied : ErrorCode.TokenInvalidated);
            await Assert.That(original.SafeDetail).IsEqualTo(scenario == 0 ? CreatorDenial : MissingLifetime);
        }
        var retained = owner.Outcome(operation);
        var entry = owner.Database.ReadRetainedEntry(operation);
        var journal = owner.Database.ReadJournalCut();
        await Assert.That(journal.CommittedIndex >= entry.Index).IsTrue();
        await Assert.That(owner.Database.Database.LastApplied >= entry.Index).IsTrue();
        var state = ReplicaSubmissionOutcomeState.Read(owner.Database);
        var observation = Assert.ThrowsExactly<KeyLoadException>(() => owner.Database.Database.ResolveSubmissionOutcome(operation));
        await Assert.That(observation.Code).IsEqualTo(scenario == 1 ? ErrorCode.TokenInvalidated : ErrorCode.PermissionDenied);
        await Assert.That(observation.Message).IsEqualTo(scenario == 2 ? ChangedPolicy : scenario == 0 ? CreatorDenial : MissingLifetime);
        var expected = new OperationResult(null, observation.Code, observation.Message);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected,
            owner.Database.Database.ResolveAppliedSubmissionOutcome(entry.Operation!));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected,
            owner.Database.Database.ResolveAppliedSubmissionOutcome(entry.Operation!));
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(retained, owner.Outcome(operation));
        await ReplicaAppliedOutcomeAuthorityFault.RunAsync(owner, operation, expected);
        await MissingOutcomeAsync(owner);
        await HealthyAsync(owner);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(retained, owner.Outcome(operation));
    });

    private static PrincipalRecord Configure(ReplicaSubmissionOutcomeFlow owner)
    {
        owner.Database.Configure(Resource, ResourceKind.BlobStore);
        var writer = owner.Principal with
        {
            Grants = [new(owner.Database.Partition.DatabaseId, Resource, Capability.All)],
            PolicyEpoch = checked(owner.Principal.PolicyEpoch + 1)
        };
        return owner.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(writer)).Get<PrincipalRecord>();
    }

    private static ReplicatedOperation Issue<T>(ReplicaSubmissionOutcomeFlow owner, OperationKind kind,
        T request, Guid id, string principal = "root")
        => owner.Database.Database.CreateNativeOperation(kind, id, principal,
            owner.Database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));

    private static async Task MissingOutcomeAsync(ReplicaSubmissionOutcomeFlow owner)
    {
        var request = new BeginBlobUploadRequest(Guid.NewGuid(),
            new(owner.Database.Partition, Resource, "never-submitted"), Guid.NewGuid(), 1, 0);
        var operation = Issue(owner, OperationKind.BeginBlobUpload, request, request.CommandId);
        var state = ReplicaSubmissionOutcomeState.Read(owner.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            owner.Database.Database.ResolveAppliedSubmissionOutcome(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(failure.Message).IsEqualTo("The applied command has no durable outcome.");
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
    }

    private static async Task HealthyAsync(ReplicaSubmissionOutcomeFlow owner)
    {
        var blob = new BlobRef(owner.Database.Partition, Resource, "healthy");
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, 1, 0);
        var originalBegin = owner.Submit(Issue(owner, OperationKind.BeginBlobUpload, begin, begin.CommandId));
        await Assert.That(originalBegin.Get<BlobCommitResult<BlobUploadInfo>>().Value.Status).IsEqualTo(BlobUploadStatus.Active);
        byte[] bytes = [43];
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var partOperation = Issue(owner, OperationKind.WriteBlobPart, part, part.CommandId);
        var originalPart = owner.Submit(partOperation);
        var staged = originalPart.Get<BlobCommitResult<BlobUploadInfo>>().Value;
        await Assert.That(staged.StoredBytes).IsEqualTo(1L);
        await Assert.That(staged.NextOrdinal).IsEqualTo(1);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, staged.IntegrityHash);
        var completeOperation = Issue(owner, OperationKind.CompleteBlobUpload, complete, complete.CommandId);
        var original = owner.Submit(completeOperation);
        var published = original.Get<BlobCommitResult<BlobMetadata>>();
        var literal = new BlobMetadata(blob, 1, upload, 1, 1, staged.IntegrityHash, new RowAccess(), completeOperation.EvaluatedAt);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(literal), NativeSerialization.Serialize(published.Value));
        await Assert.That(published.Receipt.CommandId).IsEqualTo(complete.CommandId);
        var state = ReplicaSubmissionOutcomeState.Read(owner.Database);
        var range = new global::KeyLoad.Core.Features.BlobStorage.BlobStorageOperations(owner.Database.Database)
            .Read("root", new BlobReadRequest(blob, 1, 0, 1));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(new BlobReadResult(literal, 0, bytes)),
            NativeSerialization.Serialize(range));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original,
            owner.Database.Database.ResolveAppliedSubmissionOutcome(completeOperation));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, owner.Submit(completeOperation));
        var changed = Issue(owner, OperationKind.CompleteBlobUpload,
            complete with
            {
                ExpectedIntegrityHash = new string('0', 64)
            }, complete.CommandId);
        var refused = owner.Database.Database.ResolveAppliedSubmissionOutcome(changed);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(refused.SafeDetail).IsEqualTo("The command ID was already used with different content.");
        await Assert.That(refused.Json).IsNull();
        await Assert.That(refused.NativeValue).IsNull();
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
    }
}
