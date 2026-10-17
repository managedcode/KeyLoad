using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseSchemaTests
{
    [Test]
    public async Task PhaseNamesAndIdsMatchTheClosedThirtyTwoSlotSchema()
    {
        DatabasePhaseKind[] expected =
        [
            DatabasePhaseKind.PublicAuthenticationDispatch,
            DatabasePhaseKind.PublicOperationDispatch,
            DatabasePhaseKind.AuthorizedAuthenticationBarrier,
            DatabasePhaseKind.AuthorizedOperationBarrier,
            DatabasePhaseKind.AuthorizedReadCapability,
            DatabasePhaseKind.ReadTransportReady,
            DatabasePhaseKind.LeaderBarrierTotal,
            DatabasePhaseKind.ReadRoundsWait,
            DatabasePhaseKind.ReadRoundsHold,
            DatabasePhaseKind.SubmitRoundsWait,
            DatabasePhaseKind.SubmitRoundsHold,
            DatabasePhaseKind.HeartbeatRoundsHold,
            DatabasePhaseKind.QuorumFollowerAwait,
            DatabasePhaseKind.RoundCommitFinalize,
            DatabasePhaseKind.FollowerSynchronization,
            DatabasePhaseKind.ReplicaProtocolGateWait,
            DatabasePhaseKind.ReplicaProtocolGateHold,
            DatabasePhaseKind.ReplicaRequestEncode,
            DatabasePhaseKind.ReplicaTransportAwait,
            DatabasePhaseKind.ReplicaReplyDecode,
            DatabasePhaseKind.ReplicaDiscoveryResolve,
            DatabasePhaseKind.OrleansReplicaExchangeAwait,
            DatabasePhaseKind.ReplicaReceiverTotal,
            DatabasePhaseKind.CanonicalApplyWait,
            DatabasePhaseKind.CanonicalApplyGateWait,
            DatabasePhaseKind.CanonicalApplyGateHold,
            DatabasePhaseKind.CanonicalApplyBatch,
            DatabasePhaseKind.ProviderReadGateWait,
            DatabasePhaseKind.ProviderWriteGateWait,
            DatabasePhaseKind.ProviderWriteGateHold,
            DatabasePhaseKind.AtomicJournalWriteFlush,
            DatabasePhaseKind.NativeTreeMutation
        ];

        var actual = Enum.GetValues<DatabasePhaseKind>();
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Length).IsEqualTo(32);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That((int)expected[index]).IsEqualTo(index);
        }
    }

    [Test]
    public async Task OutcomesAndQualityFlagsMatchTheClosedNumericSchema()
    {
        DatabasePhaseOutcome[] expectedOutcomes =
        [
            DatabasePhaseOutcome.Completed,
            DatabasePhaseOutcome.Rejected,
            DatabasePhaseOutcome.Busy,
            DatabasePhaseOutcome.CallerCancelled,
            DatabasePhaseOutcome.DeadlineOrStopping,
            DatabasePhaseOutcome.Faulted
        ];
        DatabaseProfileQuality[] expectedQuality =
        [
            DatabaseProfileQuality.None,
            DatabaseProfileQuality.InvalidDimension,
            DatabaseProfileQuality.InvalidElapsed,
            DatabaseProfileQuality.SaturatedCounter,
            DatabaseProfileQuality.ContentionDropped,
            DatabaseProfileQuality.SnapshotOverflow
        ];
        int[] expectedQualityValues = [0, 1, 2, 4, 8, 16];

        await Assert.That(typeof(DatabaseProfileQuality).IsDefined(typeof(FlagsAttribute), false)).IsTrue();
        var actualOutcomes = Enum.GetValues<DatabasePhaseOutcome>();
        var actualQuality = Enum.GetValues<DatabaseProfileQuality>();
        await Assert.That(actualOutcomes.SequenceEqual(expectedOutcomes)).IsTrue();
        await Assert.That(actualQuality.SequenceEqual(expectedQuality)).IsTrue();
        await Assert.That(actualOutcomes.Length).IsEqualTo(6);
        for (var index = 0; index < expectedOutcomes.Length; index++)
        {
            await Assert.That((int)expectedOutcomes[index]).IsEqualTo(index);
        }

        await Assert.That(expectedQuality.Length).IsEqualTo(expectedQualityValues.Length);
        for (var index = 0; index < expectedQuality.Length; index++)
        {
            await Assert.That((int)expectedQuality[index]).IsEqualTo(expectedQualityValues[index]);
        }
    }
}
