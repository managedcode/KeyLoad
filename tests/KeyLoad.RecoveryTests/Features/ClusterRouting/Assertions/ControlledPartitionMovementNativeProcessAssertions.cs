using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessAssertions
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const long EmptyCut = 0;
    private const int PublishedParts = 1;
    private const int NoExaminedRecords = 0;
    private const string PutDocumentKind = "putDocument";
    private const string Principal = "root";
    private const string HealthyDocument = "knowledge-followup";
    private const string HealthyJson = "{\"text\":\"healthy\"}";
    private static readonly Guid HealthyId = Guid.Parse("2e9455b1-38d7-4c80-a56f-d5596e36b7ca");

    internal static async Task<(CommitReceipt Seed, DateTimeOffset RecordedAt)> SeedAsync(
        ControlledPartitionMovementNativeHost host, GrainOperationReply original, CancellationToken cancellationToken)
    {
        var entry = ControlledPartitionMovementNativeProcessEntry.Read(host,
            ControlledPartitionMovementProcessLiteralCorpus.SeedId, cancellationToken);
        var operation = entry.Operation ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        var expected = new CommitReceipt(ControlledPartitionMovementProcessLiteralCorpus.SeedId,
            new(host.Store.Identity.Incarnation, ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId,
                entry.Index, FirstRevision), ControlledPartitionMovementProcessLiteralCorpus.ExpectedMutations(),
            DurabilityProfile.QuorumProcessDurable);
        await ReplyAsync(original, expected);
        await ControlledPartitionMovementProcessModelAssertions.ReadAsync(host, operation.EvaluatedAt, host.Store.Position, cancellationToken);
        return (expected, operation.EvaluatedAt);
    }

    internal static async Task<BlobCommitResult<BlobMetadata>> BlobAsync(ControlledPartitionMovementNativeHost host,
        CompleteBlobUploadRequest request, GrainOperationReply original, CancellationToken cancellationToken)
    {
        var entry = ControlledPartitionMovementNativeProcessEntry.Read(host, request.CommandId, cancellationToken);
        var operation = entry.Operation ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        var incarnation = host.Store.Identity.Incarnation;
        var metadata = new BlobMetadata(ControlledPartitionMovementProcessLiteralCorpus.Blob, FirstRevision,
            ControlledPartitionMovementProcessLiteralCorpus.BlobUploadId,
            ControlledPartitionMovementProcessLiteralCorpus.BlobBytes().Length, PublishedParts,
            ControlledPartitionMovementProcessLiteralCorpus.BlobIntegrityHash(incarnation), new RowAccess(), operation.EvaluatedAt, false);
        var expected = new BlobCommitResult<BlobMetadata>(new(request.CommandId,
            new(incarnation, ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId, entry.Index, FirstRevision),
            [], DurabilityProfile.QuorumProcessDurable), metadata);
        await ReplyAsync(original, expected);
        await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(host, metadata, cancellationToken);
        return expected;
    }

    internal static async Task<PartitionMovePhaseResult> PreparedAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, PartitionMovementAuthenticatedReply reply, CancellationToken cancellationToken)
    {
        var entry = ControlledPartitionMovementNativeProcessEntry.Read(owners.Source, original.CommandId, cancellationToken);
        var placement = original.Envelope.SourcePlacement;
        var intent = new PartitionMoveIntent(Version, ControlledPartitionMovementProcessPrepareRequest.MoveId,
            ControlledPartitionMovementProcessLiteralCorpus.Partition, Principal, FirstRevision, placement, owners.Destination.Owner, entry.Index);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(intent)));
        var control = new PartitionMoveControlRecord(Version, ControlledPartitionMovementProcessPrepareRequest.MoveId,
            ControlledPartitionMovementProcessLiteralCorpus.Partition, Principal, FirstRevision, placement, owners.Destination.Owner,
            PartitionMovePhase.Prepared, EmptyCut, entry.Index, null, null, null);
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementProcessPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlPrepare, new(original.CommandId, owners.Control.Owner, entry.Index, digest),
            control, null, null, null);
        await ReplyAsync(reply.Value.Reply, expected);
        return expected;
    }

    internal static async Task OutcomeAsync(ReadOnlyMemory<byte> originalBytes, PartitionMovePhaseResult expected)
    {
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(originalBytes.Span);
        await Assert.That(reply.Reply.Error).IsNull();
        await Assert.That(reply.Reply.SafeDetail).IsNull();
        var witness = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementOutcomeWitness;
        await Assert.That(witness).IsNotNull();
        var actual = witness ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        await Assert.That(actual.ReadBytes).IsGreaterThan(EmptyCut);
        await Assert.That(actual.ExaminedRecords).IsGreaterThan(NoExaminedRecords);
        await Assert.That(actual.Result.Error).IsNull();
        await Assert.That(actual.Result.SafeDetail).IsNull();
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(expected, actual.Result.Get<PartitionMovePhaseResult>());
    }

    internal static async Task<CommitReceipt> HealthyAsync(ControlledPartitionMovementNativeHost host,
        CommitReceipt? retained, CancellationToken cancellationToken)
    {
        var request = new CommandRequest(HealthyId, ControlledPartitionMovementProcessLiteralCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementProcessLiteralCorpus.Collection, HealthyDocument, HealthyJson)]);
        var reply = await host.SubmitAsync(OperationKind.Batch, HealthyId, Principal, NativeSerialization.Serialize(request), cancellationToken);
        var entry = ControlledPartitionMovementNativeProcessEntry.Read(host, HealthyId, cancellationToken);
        var expected = retained ?? new CommitReceipt(HealthyId,
            new(host.Store.Identity.Incarnation, request.Partition.AtomicPartitionId, entry.Index, FirstRevision),
            [new(PutDocumentKind, ControlledPartitionMovementProcessLiteralCorpus.Collection, HealthyDocument, FirstRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await ReplyAsync(reply, expected);
        var reference = new EntityRef(request.Partition, ControlledPartitionMovementProcessLiteralCorpus.Collection, HealthyDocument);
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(
            new DocumentResult(reference, FirstRevision, HealthyJson, false, []),
            host.Database.GetDocument(Principal, reference, cancellationToken: cancellationToken));
        return expected;
    }

    internal static async Task ReplyAsync<T>(GrainOperationReply original, T expected)
    {
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(original.Payload).Value;
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
