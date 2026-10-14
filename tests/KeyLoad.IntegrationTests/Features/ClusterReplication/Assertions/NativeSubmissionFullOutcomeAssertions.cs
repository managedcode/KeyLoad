using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionFullOutcomeAssertions
{
    internal static async Task RequireAsync(C1SubmissionOutcomeInspectionReceipt receipt,
        Guid nodeId, Guid incarnation, RequestCqrsPhaseFaultIdentity identity,
        CommandRequest command, RequestCqrsFaultRetryResult retry)
    {
        await Assert.That(receipt.Version).IsEqualTo(C1SubmissionOutcomeInspectionProtocol.Version);
        await Assert.That(receipt.NodeId).IsEqualTo(nodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(incarnation);
        await Assert.That(receipt.FormatVersion > 0).IsTrue();
        await Assert.That(receipt.Position > 0).IsTrue();
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, command.Partition);
        var expectedKey = CommandOutcomeKeyResolver.ForNew(identity.PrincipalId, command.CommandId, scope).Key;
        await Assert.That(receipt.OutcomeKey.AsSpan().SequenceEqual(expectedKey)).IsTrue();
        var fingerprint = NativeOperationFingerprint.Compute(command.CommandId, OperationKind.Batch,
            identity.PrincipalId, JsonDefaults.Serialize(command));
        var expected = new StoredOutcome(fingerprint, incarnation,
            RequestCqrsPhaseFaultProvisioning.InitialPolicyEpoch,
            new OperationResult(null) { NativeValue = retry.SdkReceipt })
        {
            ScopeKind = CommandOutcomeScopeKind.Partition,
            Partition = command.Partition
        };
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(receipt.NativeStoredOutcome);
        await Assert.That(outcome.Fingerprint).IsEqualTo(fingerprint);
        await Assert.That(outcome.Incarnation).IsEqualTo(incarnation);
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(RequestCqrsPhaseFaultProvisioning.InitialPolicyEpoch);
        await Assert.That(outcome.ScopeKind).IsEqualTo(scope.Kind);
        await Assert.That(outcome.Partition).IsEqualTo(command.Partition);
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(outcome.OnlineTextAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferFailureAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferAuthority).IsNull();
        await Assert.That(NativeSerialization.Serialize(outcome.Result).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected.Result))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(outcome.Result.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(retry.McpReceipt))).IsTrue();
        await Assert.That(receipt.NativeStoredOutcome.AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }
}
