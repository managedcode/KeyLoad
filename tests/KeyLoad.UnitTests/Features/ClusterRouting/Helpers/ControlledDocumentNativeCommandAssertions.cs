using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Compares independent complete receipts/document values and actual retained replay bytes after cold ownership.</summary>
internal static class ControlledDocumentNativeCommandAssertions
{
    private const long NativeApplyStep = 1;
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;
    private const string UpdatedJson = "{\"state\":\"derived\",\"text\":\"Київ knowledge\"}";
    private const string Collection = "movement-documents";
    private const string DocumentId = "knowledge-1";
    private const string Principal = "root";
    private static readonly Guid OriginalId = Guid.Parse("bc107e1c-4857-4a9b-aeb8-c218d2a22156");
    private const string PartitionKey = "partition-a";
    private static readonly PartitionRef Partition = new("system", "movement", "knowledge", PartitionKey);
    private const string ChangedJson = "{\"state\":\"changed\"}";
    private const string ConflictDetail = "The command ID was already used with different content.";
    private const string MutationKind = "putDocument";

    internal static async Task CompleteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ReplicatedOperation original,
        PartitionControlDocumentCommandContext context, PartitionControlEffectPayload effect,
        long originalTargetIndex, CancellationToken token)
    {
        await Assert.That(original.Id).IsEqualTo(OriginalId);
        await Assert.That(original.PrincipalId).IsEqualTo(Principal);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.Batch);
        var expectedRequest = new CommandRequest(OriginalId, Partition,
            [new PutDocument(Collection, DocumentId, UpdatedJson, InitialRevision, ExplicitReplacement: true)]);
        await Assert.That(original.NativePayload.Span.SequenceEqual(NativeSerialization.Serialize(expectedRequest))).IsTrue();
        var published = context.Control.PublishedPlacement
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var expectedToken = new CommitToken(target.Store.Identity.Incarnation,
            Partition.AtomicPartitionId,
            checked(originalTargetIndex + NativeApplyStep), published.PlacementEpoch);
        var expected = new CommitReceipt(OriginalId, expectedToken,
            [new MutationReceipt(MutationKind, Collection,
                DocumentId, UpdatedRevision)],
            DurabilityProfile.ProcessDurable);
        await Assert.That(JsonSerializer.Serialize(effect.OriginalResult.Get<CommitReceipt>(), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        var expectedEffect = expected with
        {
            CommandId = ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.Effect)
        };
        await Assert.That(JsonSerializer.Serialize(effect.Receipt, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expectedEffect, JsonDefaults.Options));
        var first = Resolve(source, original, token);
        await Assert.That(first.Error).IsNull();
        await Assert.That(first.SafeDetail).IsEqualTo(effect.OriginalResult.SafeDetail);
        await Assert.That(first.Json).IsEqualTo(effect.OriginalResult.Json);
        await Assert.That(JsonSerializer.Serialize(first.Get<CommitReceipt>(), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await DocumentAsync(source, target, expected.Token, token);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var firstBytes = NativeSerialization.Serialize(first);
        await ChangedPayloadAsync(source, original, token);
        await StableAsync(source, target, original, firstBytes, sourceImage, targetImage,
            sourcePosition, targetPosition, token);
        source.Reopen();
        target.Reopen();
        await StableAsync(source, target, original, firstBytes, sourceImage, targetImage,
            sourcePosition, targetPosition, token);
        await DocumentAsync(source, target, expected.Token, token);
    }

    private static async Task ChangedPayloadAsync(ControlledPartitionMovementNode source,
        ReplicatedOperation original, CancellationToken token)
    {
        var changed = new CommandRequest(original.Id, Partition,
            [new PutDocument(Collection, DocumentId,
                ChangedJson, UpdatedRevision, ExplicitReplacement: true)]);
        var issued = source.Database.CreateNativeOperation(OperationKind.Batch, original.Id, original.PrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(changed));
        var work = new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.TryCaptureControlledDocumentCommand(
            issued.PrincipalId, issued, work));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(rejected.Message).IsEqualTo(ConflictDetail);
    }

    private static OperationResult Resolve(ControlledPartitionMovementNode source,
        ReplicatedOperation original, CancellationToken token)
        => source.Database.ResolveControlledDocumentOutcome(original.PrincipalId, original,
            new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token));

    private static async Task StableAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ReplicatedOperation original, byte[] originalBytes,
        string[] sourceImage, string[] targetImage, long sourcePosition, long targetPosition,
        CancellationToken token)
    {
        var replay = Resolve(source, original, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(originalBytes)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
    }

    private static async Task DocumentAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, CommitToken minimum, CancellationToken token)
    {
        var reference = new EntityRef(Partition,
            Collection, DocumentId);
        var parent = new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token);
        var frame = source.Database.CaptureControlledDocumentRead(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, minimum, source.Database.EvaluationClock.GetUtcNow() +
                TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds), parent);
        var bytes = Math.Min(target.Database.Limits.MaxBatchBytes, target.Database.Limits.MaxQueryReadBytes);
        var records = Math.Min(target.Database.Limits.MaxBatchMutations, target.Database.Limits.MaxScanRecords);
        var originalGrant = parent.CreateReadGrant(bytes, records);
        var receiver = new ReadExecutionBudget(Options.Create(target.Database.Limits), target.Database.EvaluationClock, token);
        var grant = receiver.CreateReadGrant(bytes, records);
        var actual = target.Database.ReadControlledDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            frame, receiver, grant, token);
        parent.ImportReadGrant(originalGrant, grant.ReadBytes, grant.ExaminedRecords);
        source.Database.ValidateControlledDocumentRead(frame, parent);
        var expected = new DocumentResult(reference, UpdatedRevision,
            UpdatedJson, false, []);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }
}
