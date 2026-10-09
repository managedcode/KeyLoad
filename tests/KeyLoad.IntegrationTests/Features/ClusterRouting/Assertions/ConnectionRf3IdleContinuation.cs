using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3IdleContinuation
{
    internal static async Task VerifyAsync(ConnectionRf3Scenario scenario, ConnectionRf3Caller caller,
        KeyLoadClient independent, ConnectionProbeWitness original, RequestCqrsProbeMarkerRecord marker,
        CancellationToken cancellationToken)
    {
        var idle = await ConnectionRf3WitnessReader.WaitAsync(scenario.Controls, marker, scenario.Discovery,
            closed: true, cancellationToken, new ConnectionRf3Options().IdleObservationTimeout);
        await Assert.That(idle.ConnectionId).IsEqualTo(original.ConnectionId);
        await Assert.That(idle.GrainId).IsEqualTo(original.GrainId);
        await Assert.That(idle.ActivationId).IsEqualTo(original.ActivationId);
        await Assert.That(idle.SelectedActivationCount).IsEqualTo(ConnectionRf3Protocol.Absent);
        var reference = ConnectionRf3PublicOperations.Reference(scenario.SecondIdentity);
        var following = await scenario.ObserveAsync(scenario.SecondIdentity, Guid.Empty, GrainReadKind.Document,
            () => independent.GetAsync(reference, cancellationToken), cancellationToken);
        var document = await McpCallerAssertions.SdkSuccessAsync(following.Result);
        await ConnectionRf3PublicOperations.DocumentAsync(new(document, null), scenario.SecondIdentity, following.Witness,
            RequestCqrsRf3Protocol.ChangedDocumentJson, ConnectionRf3Protocol.UpdatedRevision);
        await Assert.That(following.Witness.ConnectionId).IsNotEqualTo(original.ConnectionId);
        await Assert.That(following.Witness.GrainId).IsNotEqualTo(original.GrainId);
        await Assert.That(following.Witness.ActivationId).IsNotEqualTo(original.ActivationId);
        await scenario.VerifyDocumentAsync(scenario.Identity, RequestCqrsRf3Protocol.DocumentJson,
            ConnectionRf3Protocol.InitialRevision, cancellationToken);
        await scenario.VerifyDocumentAsync(scenario.SecondIdentity, RequestCqrsRf3Protocol.ChangedDocumentJson,
            ConnectionRf3Protocol.UpdatedRevision, cancellationToken);
        var disconnected = await scenario.CloseAsync(caller, following.Marker, cancellationToken);
        await Assert.That(disconnected.ConnectionId).IsEqualTo(following.Witness.ConnectionId);
        await Assert.That(disconnected.ActivationId).IsEqualTo(following.Witness.ActivationId);
        await Assert.That(disconnected.SelectedActivationCount).IsEqualTo(ConnectionRf3Protocol.Absent);
    }
}
