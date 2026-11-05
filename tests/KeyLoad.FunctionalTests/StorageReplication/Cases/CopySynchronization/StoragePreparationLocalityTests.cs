using System.Net;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class StoragePreparationLocalityTests
{
    private const string Collection = "prepared-documents";
    private const string Document = "original";
    private const string Json = "{\"value\":42}";

    [Test]
    public Task PreparationUsesTheCoordinatorsSelectedLocalCopyAndRetainsBothOutcomes()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var commandId = Guid.NewGuid();
        var observation = new StoragePreparationObservation(commandId);
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, observation, commandId, token),
            token, configureServices: (_, services, _) => services.AddSingleton<IOutgoingGrainCallFilter>(observation));
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, StoragePreparationObservation observation,
        Guid commandId, CancellationToken token)
    {
        var source = cluster.Nodes[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(source.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var creation = Guid.NewGuid();
        source.Database.Configure(Collection, ResourceKind.Collection, id: creation);
        var before = source.Database.ReadCanonicalCut().AppliedPosition;
        var command = new CommandRequest(commandId, source.Database.Partition, [new PutDocument(Collection, Document, Json)]);
        var applied = source.Database.Submit(OperationKind.Batch, command, id: commandId, cancellationToken: bounded);
        await Assert.That(applied.IsSuccess).IsTrue();
        await Assert.That(applied.Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        var call = observation.Calls.Single();
        await Assert.That(call.Target).IsEqualTo(call.Source);
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

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalCopyBecomesPrimaryOnlyAfterItsCanonicalCutMatches(bool differentIncarnation)
    {
        var incarnation = Guid.NewGuid();
        var remote = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 11111), 1);
        var local = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 11112), 1);
        StorageCopyOwner[] owners = [new(remote, "remote", new(incarnation, 10)),
            new(local, "local", new(differentIncarnation ? Guid.NewGuid() : incarnation, differentIncarnation ? 10 : 9))];
        StorageCopyPrimarySelection.PreferLocal(owners, local);
        await Assert.That(owners[0].Address).IsEqualTo(remote);
        owners[1] = owners[1] with { Cut = owners[0].Cut };
        StorageCopyPrimarySelection.PreferLocal(owners, local);
        await Assert.That(owners[0].Address).IsEqualTo(local);
        await Assert.That(owners[1].Address).IsEqualTo(remote);
        await Assert.That(owners.Select(owner => owner.Cut).Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task LocalCopyOutsideTheSelectedOwnerSetDoesNotReplaceARequiredCopy()
    {
        var remote = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 11111), 1);
        var local = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 11112), 1);
        StorageCopyOwner[] owners = [new(remote, "remote", new(Guid.NewGuid(), 10))];
        StorageCopyPrimarySelection.PreferLocal(owners, local);
        await Assert.That(owners.Length).IsEqualTo(1);
        await Assert.That(owners[0].Address).IsEqualTo(remote);
    }
}
