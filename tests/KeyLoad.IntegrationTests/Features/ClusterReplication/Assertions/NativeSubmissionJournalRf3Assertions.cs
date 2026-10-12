using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionJournalRf3Assertions
{
    internal static async Task RequireAsync(C1SubmissionJournalInspectionReceipt receipt,
        C1SubmissionJournalInspectionRequest request, CommandRequest command,
        RequestCqrsPhaseFaultIdentity identity, RequestCqrsProbeMarkerRecord marker, long canonicalPosition)
    {
        await Assert.That(receipt.Version).IsEqualTo(C1SubmissionJournalInspectionProtocol.Version);
        await Assert.That(receipt.CanonicalNodeId).IsEqualTo(request.ExpectedCanonicalNodeId);
        await Assert.That(receipt.ReplicaNodeId != Guid.Empty && receipt.ReplicaNodeId != receipt.CanonicalNodeId).IsTrue();
        await Assert.That(receipt.Incarnation).IsEqualTo(request.Incarnation);
        await Assert.That(receipt.CanonicalPosition).IsEqualTo(canonicalPosition);
        await Assert.That(receipt.ReplicaPosition > 0).IsTrue();
        await Assert.That(marker.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.NativeHardState.Length <= C1SubmissionJournalInspectionProtocol.MaximumNativeRecordBytes).IsTrue();
        await Assert.That(receipt.NativeEntry.Length <= C1SubmissionJournalInspectionProtocol.MaximumNativeRecordBytes).IsTrue();
        var state = ReplicaProtocolCodec.Deserialize<ReplicaHardState>(receipt.NativeHardState);
        var entry = ReplicaProtocolCodec.Deserialize<ReplicaEntry>(receipt.NativeEntry);
        await RequireStateAsync(state, entry, request).ConfigureAwait(false);
        await Assert.That(receipt.NativeHardState.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(state))).IsTrue();
        await Assert.That(receipt.NativeEntry.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(entry))).IsTrue();
        var operation = entry.Operation ?? throw NativeSubmissionFaultRf3Owner.Missing();
        await RequireOperationAsync(operation, command, identity, request.Incarnation).ConfigureAwait(false);
    }

    private static async Task RequireStateAsync(ReplicaHardState state, ReplicaEntry entry,
        C1SubmissionJournalInspectionRequest request)
    {
        await Assert.That(state.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
        await Assert.That(state.Incarnation).IsEqualTo(request.Incarnation);
        await Assert.That(state.Term >= request.EntryTerm).IsTrue();
        await Assert.That(state.LastIndex >= state.CommittedIndex && state.CommittedIndex >= request.EntryIndex).IsTrue();
        await Assert.That(state.VotedFor is null || request.Voters.Contains(state.VotedFor, StringComparer.Ordinal)).IsTrue();
        await Assert.That(entry.Index).IsEqualTo(request.EntryIndex);
        await Assert.That(entry.Term).IsEqualTo(request.EntryTerm);
        if (state.Snapshot is { } snapshot)
        {
            await Assert.That(snapshot.Incarnation).IsEqualTo(request.Incarnation);
            await Assert.That(snapshot.Index >= 0 && snapshot.Index < request.EntryIndex).IsTrue();
            await Assert.That(snapshot.Term >= 0 && snapshot.Term <= state.Term).IsTrue();
        }
    }

    private static async Task RequireOperationAsync(ReplicatedOperation operation, CommandRequest command,
        RequestCqrsPhaseFaultIdentity identity, Guid incarnation)
    {
        await Assert.That(operation.Id).IsEqualTo(command.CommandId);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(operation.PrincipalId).IsEqualTo(identity.PrincipalId);
        await Assert.That(operation.PayloadJson).IsEqualTo(Encoding.UTF8.GetString(JsonDefaults.Serialize(command)));
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        await Assert.That(payload.Error).IsNull();
        await Assert.That(payload.SafeDetail).IsNull();
        await Assert.That(payload.Value.Span.SequenceEqual(NativeSerialization.Serialize(command))).IsTrue();
        var original = NativeCommandPayload.Read<CommandRequest>(operation);
        await Assert.That(original.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(original.Partition).IsEqualTo(identity.Partition);
        await Assert.That(payload.Signature.Length).IsEqualTo(NativeAuthorityContract.DigestBytes);
        var authority = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span);
        await RequireAuthorityAsync(authority, payload, operation, command, incarnation).ConfigureAwait(false);
    }

    private static async Task RequireAuthorityAsync(NativeCommandAuthority authority, NativeCommandPayload payload,
        ReplicatedOperation operation, CommandRequest command, Guid incarnation)
    {
        await Assert.That(authority.Purpose).IsEqualTo(NativeAuthorityContract.Purpose);
        await Assert.That(authority.Incarnation).IsEqualTo(incarnation);
        await Assert.That(authority.OperationId).IsEqualTo(command.CommandId);
        await Assert.That(authority.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(authority.PrincipalId).IsEqualTo(operation.PrincipalId);
        var expectedFingerprint = NativeOperationFingerprint.Compute(command.CommandId, OperationKind.Batch,
            operation.PrincipalId, JsonDefaults.Serialize(command));
        await Assert.That(authority.Fingerprint).IsEqualTo(expectedFingerprint);
        await Assert.That(authority.ValueHash.Span.SequenceEqual(SHA256.HashData(NativeSerialization.Serialize(command)))).IsTrue();
        await Assert.That(authority.Error).IsNull();
        await Assert.That(authority.SafeDetail).IsNull();
        var retryHash = payload.RetryDecisions.IsEmpty ? [] : SHA256.HashData(payload.RetryDecisions.Span);
        await Assert.That(authority.RetryDecisionHash.Span.SequenceEqual(retryHash)).IsTrue();
        await Assert.That(payload.TransferProof.IsEmpty).IsTrue();
        await Assert.That(authority.TransferProofHash.IsEmpty).IsTrue();
    }
}
