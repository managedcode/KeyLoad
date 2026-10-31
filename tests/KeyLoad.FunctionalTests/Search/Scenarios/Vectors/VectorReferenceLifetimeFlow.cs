using KeyLoad.Core.Search;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using static KeyLoad.FunctionalTests.Search.VectorReferenceLifetimeProtocol;

namespace KeyLoad.FunctionalTests.Search;

/// <summary>Retires and recreates the actual source table before refusing an original typed reference without effects.</summary>
internal static class VectorReferenceLifetimeFlow
{
    internal static async Task RunAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        await VectorReferenceLifetimeCalls.SeedAsync(profile, token);
        var originalSource = VectorReferenceLifetimeCalls.Source(profile);
        var original = new CommandRequest(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [Request(originalSource with { Lifetime = null }, fresh: false)]);
        var receipt = await VectorReferenceLifetimeCalls.CommitAsync(profile, original, token);
        await VectorReferenceLifetimeReceipt.AssertAsync(profile, original, receipt, token);
        await LineageAsync(profile, originalSource);
        await Assert.That((await VectorReferenceLifetimeCalls.SearchAsync(profile, token)).Length).IsEqualTo(OneResult);
        var drop = DropPublicSubmission.Start(profile, new(DropRuntimeProtocol.Tenant,
            DropRuntimeProtocol.Database, DropRuntimeProtocol.Table), Guid.NewGuid(), token);
        _ = DropRuntimeAssertions.Value<DropTableResult>(await drop.Original);
        await DropRuntimeSeed.CreateAsync(profile, token);
        var freshSource = VectorReferenceLifetimeCalls.Source(profile);
        await Assert.That(freshSource.Lifetime).IsNotNull();
        await Assert.That(freshSource.Lifetime).IsNotEqualTo(originalSource.Lifetime);
        await RefuseSearchAsync(profile, token);
        await VectorReferenceLifetimeCalls.EventAsync(profile, fresh: true, token);
        await RefuseWriteAsync(profile, Request(originalSource, fresh: true), token);
        var healthy = new CommandRequest(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [Request(freshSource with { Lifetime = null }, fresh: true)]);
        var repaired = await VectorReferenceLifetimeCalls.CommitAsync(profile, healthy, token);
        await VectorReferenceLifetimeReceipt.AssertAsync(profile, healthy, repaired, token);
        await LineageAsync(profile, freshSource);
        for (var cold = FirstCold; cold < ColdCuts; cold++)
        {
            await profile.RestartAsync(token);
            await DropRuntimeAssertions.SameAsync(repaired,
                await VectorReferenceLifetimeCalls.CommitAsync(profile, healthy, token));
            await LineageAsync(profile, freshSource);
            var found = await VectorReferenceLifetimeCalls.SearchAsync(profile, token);
            await Assert.That(found.Length).IsEqualTo(OneResult);
            await Assert.That(found.Single().Document.Reference.Id).IsEqualTo(DropRuntimeProtocol.Row);
        }
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
    }

    private static async Task LineageAsync(DropRuntimeProfile profile, EntityRef expected)
    {
        var key = VectorProjectionKeys.Lineage(DropRuntimeProtocol.Partition, Target, Field, DropRuntimeProtocol.Row);
        var lineage = profile.Source.Store.Read(view => view.GetRecord<VectorProjectionLineage>(key));
        await Assert.That(lineage).IsNotNull();
        await DropRuntimeAssertions.SameAsync(expected, lineage!.SourceDocument);
    }

    private static async Task RefuseSearchAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => VectorReferenceLifetimeCalls.SearchAsync(profile, token));
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }

    private static async Task RefuseWriteAsync(DropRuntimeProfile profile, ApplyVectorProjection request, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var command = new CommandRequest(Guid.NewGuid(), DropRuntimeProtocol.Partition, [request]);
        var denied = await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, command, token);
        await Assert.That(denied.Error).IsEqualTo((ErrorCode?)ErrorCode.OwnershipLost);
        await Assert.That(denied.Result).IsNull();
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }
}
