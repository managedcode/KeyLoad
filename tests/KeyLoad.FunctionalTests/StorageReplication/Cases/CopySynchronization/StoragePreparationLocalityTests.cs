using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class StoragePreparationLocalityTests
{
    private const string Collection = "prepared-documents";
    private const string Document = "original";
    private const string Json = "{\"value\":42}";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OnePrimaryPreparesTheCommandAndEveryCopyRetainsItsOriginalOutcome(bool remote)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var commandId = Guid.NewGuid();
        var observation = new StoragePreparationObservation(commandId);
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, observation, commandId, remote, token),
            token, configureServices: (_, services, _) => services.AddSingleton<IOutgoingGrainCallFilter>(observation));
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, StoragePreparationObservation observation,
        Guid commandId, bool remote, CancellationToken token)
    {
        var source = cluster.Nodes[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(source.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var creation = Guid.NewGuid();
        source.Database.Configure(Collection, ResourceKind.Collection, id: creation);
        await StorageCoordinatorPlacement.ActivateAsync(cluster, remote, bounded);
        var before = source.Database.ReadCanonicalCut().AppliedPosition;
        var command = new CommandRequest(commandId, source.Database.Partition, [new PutDocument(Collection, Document, Json)]);
        var applied = source.Database.Submit(OperationKind.Batch, command, id: commandId, cancellationToken: bounded);
        await Assert.That(applied.IsSuccess).IsTrue();
        await Assert.That(applied.Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        await RequireTransportAsync(cluster, observation, remote);
        var replayed = source.Database.Submit(OperationKind.Batch, command, id: commandId, cancellationToken: bounded);
        await Assert.That(replayed.IsSuccess).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(replayed.Get<CommitReceipt>()).SequenceEqual(
            OrleansSerialization.Serialize(applied.Get<CommitReceipt>()))).IsTrue();
        foreach (var node in cluster.Nodes)
        {
            await Assert.That(node.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before + 1);
            var reference = node.Database.Reference(creation, Collection, Document);
            var document = node.Database.Database.GetDocument(RequestCqrsHostedProfile.Root, reference, cancellationToken: token);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Json).IsEqualTo(Json);
            await Assert.That(document.Revision).IsEqualTo(1L);
        }
        var healthyId = Guid.NewGuid();
        var healthy = new CommandRequest(healthyId, source.Database.Partition, [new PutDocument(Collection, "healthy", Json)]);
        await Assert.That(source.Database.Submit(OperationKind.Batch, healthy, id: healthyId,
            cancellationToken: bounded).Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
    }

    private static async Task RequireTransportAsync(RequestCqrsHostedCluster cluster,
        StoragePreparationObservation observation, bool remote)
    {
        await Assert.That(observation.Calls.Length).IsEqualTo(remote ? 1 : 0);
        if (remote)
        {
            var call = observation.Calls.Single();
            await Assert.That(call.Source).IsNotEqualTo(call.Target);
            await Assert.That(cluster.Nodes.Select(node => node.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress))
                .Contains(call.Target);
        }
    }
}
