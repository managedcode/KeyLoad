using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3FreshIdleWindowAssertions
{
    private const string PutDocumentKind = "putDocument";
    private const long EmptyPosition = 0;
    private const long EmptyOwnershipEpoch = 0;

    internal static async Task ReceiptAsync(CommitReceipt actual, CommandRequest command)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new(PutDocumentKind, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                ConnectionRf3Protocol.UpdatedRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Token.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(actual.Token.OwnershipEpoch).IsGreaterThan(EmptyOwnershipEpoch);
    }

    internal static async Task ObserveWindowAsync(ConnectionRf3Scenario scenario,
        RequestCqrsProbeMarkerRecord marker, long beforeRelease, CancellationToken token)
    {
        var clock = TimeProvider.System;
        var idle = new GrainRoutingOptions().ConnectionIdleTimeout;
        var poll = new ConnectionRf3Options().PollInterval;
        await Assert.That(clock.GetElapsedTime(beforeRelease) < idle).IsTrue();
        while (clock.GetElapsedTime(beforeRelease) < idle)
        {
            token.ThrowIfCancellationRequested();
            foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
            {
                var owner = scenario.Controls.NodeFor(node);
                RequestCqrsProbeFileStore.VerifyOwnerFile(owner.Directory, owner.OwnerBytes);
                var name = ConnectionProbeInventory.Name(marker.ArmId, marker.RequestId, closed: true);
                var present = File.Exists(Path.Combine(owner.Directory, name));
                if (clock.GetElapsedTime(beforeRelease) < idle)
                {
                    await Assert.That(present).IsFalse();
                }
            }
            var remaining = idle - clock.GetElapsedTime(beforeRelease);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining < poll ? remaining : poll, clock, token).ConfigureAwait(false);
            }
        }
    }

    internal static async Task ClosedAsync(ConnectionProbeWitness original, ConnectionProbeWitness closed)
    {
        await Assert.That(closed.ConnectionId).IsEqualTo(original.ConnectionId);
        await Assert.That(closed.GrainId).IsEqualTo(original.GrainId);
        await Assert.That(closed.ActivationId).IsEqualTo(original.ActivationId);
        await Assert.That(closed.SelectedActivationCount).IsEqualTo(ConnectionRf3Protocol.Absent);
        await Assert.That(closed.Closed).IsTrue();
    }
}
