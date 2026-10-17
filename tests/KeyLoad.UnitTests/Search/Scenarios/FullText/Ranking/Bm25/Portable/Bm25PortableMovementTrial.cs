using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableMovementTrial
{
    private readonly Bm25PortableMovementBorrow borrow = new();
    private readonly List<Bm25PortableMovementPurposeEvidence> purposeEvidence = [];
    private ControlledPartitionMovementNativeHosts? owners;
    private Task<KeyLoad.Clustering.GrainOperationReply>? original;

    internal static async Task RunAsync(CancellationToken token)
    {
        var trial = new Bm25PortableMovementTrial();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(token), failures);
        await trial.CloseAsync(failures);
        if (failures.Count != 0)
        {
            ServerFailureObserver.Observe(() => trial.WriteEvidence(failures[0]), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        owners = await ControlledPartitionMovementNativeHosts.CreateAsync(index =>
        {
            var evidence = new Bm25PortableMovementPurposeEvidence(index, borrow);
            purposeEvidence.Add(evidence);
            return evidence;
        }, token);
        var published = await Bm25PortableMovementSeed.CreateAsync(owners, token);
        var seededEntry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(owners.Source,
            ControlledPartitionMovementCorpus.SeedId, token);
        var seededAt = seededEntry.Operation!.EvaluatedAt;
        var placement = owners.Source.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            new(1, published.Head.Partition));
        var request = new PartitionMoveRequest(Guid.NewGuid(), published.Head.Partition,
            owners.Destination.Owner.PhysicalShardId, placement.Revision, PartitionMoveMode.Transfer);
        original = ControlledPartitionMovementNativeRetireProducer.Start(owners, request, token);
        var first = await Task.WhenAny(borrow.Entered, original).WaitAsync(token);
        if (first == original)
        {
            _ = await original;
            throw new InvalidOperationException("The original native parent finished before its Retire borrow.");
        }
        var sealedOperation = await borrow.Entered;
        var phase = global::KeyLoad.Core.Features.ClusterRouting.Serialization.PartitionMovementNativePayload.Read(sealedOperation);
        await Assert.That(phase.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Bm25PortableMovementAssertions.HeadAsync(owners.Target, published.Head, published.Bytes, token);
        await Bm25PortableMovementRefusals.RunAsync(owners, published.Head, published.Bytes, token);
        borrow.Release();
        var reply = await original;
        original = null;
        await borrow.JoinAsync();
        var completed = await Bm25PortableMovementAssertions.ResultAsync(reply, request);
        await Bm25PortableMovementAssertions.HeadAsync(owners.Target, published.Head, published.Bytes, token);
        await ControlledPartitionMovementNativeRetireProducer.CompleteAsync(owners, request, completed, token);
        await ControlledPartitionMovementNativeModels.ReadAsync(owners.Target, seededAt, token);
        await Bm25PortableMovementAssertions.HistoricalAsync(owners, published.Head, published.Bytes, token);
        await Bm25PortableMovementAssertions.HealthyAsync(owners.Target, token);
    }

    private void WriteEvidence(Exception primary)
    {
        foreach (var evidence in purposeEvidence)
        {
            evidence.Write(Console.Error, primary);
        }
    }

    private async Task CloseAsync(List<Exception> failures)
    {
        borrow.Release();
        if (owners is not { } owned)
        {
            return;
        }
        if (failures.Count != 0)
        {
            owned.RetainRoots();
        }
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(CancellationToken.None), failures);
        if (original is { } producer)
        {
            await ServerFailureObserver.ObserveAsync(async () => { _ = await producer.ConfigureAwait(false); }, failures);
        }
        await ServerFailureObserver.ObserveAsync(borrow.JoinAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
    }
}
