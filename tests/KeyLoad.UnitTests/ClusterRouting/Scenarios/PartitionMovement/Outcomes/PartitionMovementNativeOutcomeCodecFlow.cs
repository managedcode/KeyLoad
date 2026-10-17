using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PartitionMovementNativeOutcomeCodecFlow
{
    private const int MaximumBytes = 4_096;
    private static readonly byte[] OriginalBytes = [1, 2, 3, 4];

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, string callerAddress, CancellationToken token)
    {
        var prepared = await ControlledPartitionMovementPrepareFlow.ExecuteAsync(source, target, corpus,
            runtime, admission, callerAddress, token);
        var original = ControlledPartitionMovementPrepareRequest.Create(source, runtime, corpus,
            ControlledPartitionMovementPrepareAssertions.Placement(corpus), callerAddress);
        original = original with
        {
            Envelope = original.Envelope with
            {
                ExpiresAt = prepared.ExpiresAt
            }
        };
        var targetPosition = target.Store.Position;
        var targetIndex = target.Journal.Log.State.LastIndex;
        var query = new PartitionMovementOutcomeTransportRequest(original, original.Envelope.ExpiresAt,
            Guid.NewGuid(), Math.Min(MaximumBytes, Math.Min(source.Database.Limits.MaxBatchBytes,
                source.Database.Limits.MaxQueryReadBytes)), Math.Min(source.Database.Limits.MaxBatchMutations,
                source.Database.Limits.MaxScanRecords), Math.Min(MaximumBytes, source.Database.Limits.MaxBatchBytes));
        var bytes = NativeSerialization.Serialize(query);
        await Assert.That(bytes.Length <= MaximumBytes).IsTrue();
        var decoded = NativeSerialization.Deserialize<PartitionMovementOutcomeTransportRequest>(bytes);
        await Assert.That(NativeSerialization.Serialize(decoded).SequenceEqual(bytes)).IsTrue();
        await Assert.That(NativeSerialization.Deserialize<byte[]>(NativeSerialization.Serialize(OriginalBytes))
            .SequenceEqual(OriginalBytes)).IsTrue();
        using var mac = admission.CreateControlMac();
        var wrongOutcome = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            admission.VerifyOutcomeAsync(bytes, mac.Sign(bytes, reply: false), token));
        await RequireRefusalAsync(wrongOutcome);
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, prepared.Source,
            prepared.Target, prepared.Position, prepared.Index);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        var phaseBytes = NativeSerialization.Serialize(original);
        var wrongPhase = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            admission.VerifyAsync(phaseBytes, mac.SignOutcome(phaseBytes, reply: false), token));
        await RequireRefusalAsync(wrongPhase);
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, prepared.Source,
            prepared.Target, prepared.Position, prepared.Index);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        var healthy = await admission.VerifyOutcomeAsync(bytes, mac.SignOutcome(bytes, reply: false), token);
        await Assert.That(NativeSerialization.Serialize(healthy).SequenceEqual(bytes)).IsTrue();
        var phase = await admission.VerifyAsync(phaseBytes, mac.Sign(phaseBytes, reply: false), token);
        await Assert.That(NativeSerialization.Serialize(phase).SequenceEqual(phaseBytes)).IsTrue();
        var replay = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            original with
            {
                Envelope = original.Envelope with
                {
                    Nonce = Guid.NewGuid()
                }
            }, token);
        await Assert.That(NativeSerialization.Serialize(replay.Get<PartitionMovePhaseResult>())
            .SequenceEqual(NativeSerialization.Serialize(prepared.Prepared))).IsTrue();
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, prepared.Source,
            prepared.Target, prepared.Position, prepared.Index);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
    }

    private static async Task RequireRefusalAsync(KeyLoadException? failure)
    {
        var original = failure ?? throw new InvalidOperationException("The wrong native MAC purpose did not refuse.");
        await Assert.That(original.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(original.Message).IsEqualTo(PartitionMovementPeerAdmission.InvalidProof);
    }
}
