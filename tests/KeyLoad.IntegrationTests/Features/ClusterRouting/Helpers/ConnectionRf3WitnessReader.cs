using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3WitnessReader
{
    internal static async Task<ConnectionProbeWitness> WaitAsync(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeMarkerRecord marker, IReadOnlyList<ReplicaSiloDiscovery> discovery, bool closed,
        CancellationToken cancellationToken, TimeSpan? observationTimeout = null)
    {
        var settings = new ConnectionRf3Options();
        using var timeout = new CancellationTokenSource(observationTimeout ?? settings.ClosedObservationTimeout,
            TimeProvider.System);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = bound.Token;
        var name = ConnectionProbeInventory.Name(marker.ArmId, marker.RequestId, closed);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            ConnectionProbeWitness? witness = null;
            foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
            {
                var owner = fixture.NodeFor(node);
                RequestCqrsProbeFileStore.VerifyOwnerFile(owner.Directory, owner.OwnerBytes);
                var path = Path.Combine(owner.Directory, name);
                if (!File.Exists(path))
                { continue; }
                if (witness is not null)
                { throw Invalid(); }
                witness = ConnectionProbeInventory.Read(RequestCqrsProbeFileStore.ReadRecord(path));
            }
            if (witness is not null)
            {
                await ValidateAsync(fixture, witness, marker, discovery, closed);
                return witness;
            }
            await Task.Delay(settings.PollInterval, token).ConfigureAwait(false);
        }
    }

    private static async Task ValidateAsync(RequestCqrsProbeFixture fixture, ConnectionProbeWitness value,
        RequestCqrsProbeMarkerRecord marker, IReadOnlyList<ReplicaSiloDiscovery> discovery, bool closed)
    {
        await Assert.That(value.SessionId).IsEqualTo(fixture.SessionId);
        await Assert.That(value.ArmId).IsEqualTo(marker.ArmId);
        await Assert.That(value.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(value.CommandId).IsEqualTo(marker.CommandId);
        await Assert.That(value.Voter).IsEqualTo(marker.Voter);
        await Assert.That(value.SiloAddress).IsEqualTo(marker.SiloAddress);
        await Assert.That(value.Closed).IsEqualTo(closed);
        var voter = discovery.Single(item => item.VoterId == value.Voter);
        await Assert.That(value.SiloAddress).IsEqualTo(voter.SiloAddress);
        await Assert.That(value.SelectedActivationCount)
            .IsEqualTo(closed ? ConnectionRf3Protocol.Absent : ConnectionRf3Protocol.OneConnection);
        await Assert.That(value.ClusterConnectionActivationCount).IsGreaterThanOrEqualTo(value.SelectedActivationCount);
    }

    internal static async Task SameOwnerAsync(ConnectionProbeWitness first, ConnectionProbeWitness following)
    {
        await Assert.That(first.ConnectionId).IsEqualTo(following.ConnectionId);
        await Assert.That(first.GrainId).IsEqualTo(following.GrainId);
        await Assert.That(first.ActivationId).IsEqualTo(following.ActivationId);
        await Assert.That(first.RequestId).IsNotEqualTo(following.RequestId);
        await Assert.That(first.ConnectionGrainType).IsEqualTo(following.ConnectionGrainType);
    }

    private static InvalidOperationException Invalid() => new(ConnectionRf3Protocol.NativeMismatch);
}
