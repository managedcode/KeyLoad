using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.VectorReferenceLifetimeNativeProtocol;

using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Retires and recreates the actual source table before refusing an original typed reference without effects.</summary>
internal static class VectorReferenceLifetimeNativeFlow
{
    internal static async Task RunAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await VectorReferenceLifetimeNativeCalls.SeedAsync(profile, token);
        var originalSource = VectorReferenceLifetimeNativeCalls.Source(profile);
        var original = new CommandRequest(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [Request(originalSource with { Lifetime = null }, fresh: false)]);
        var receipt = await VectorReferenceLifetimeNativeCalls.CommitAsync(profile, original, token);
        await VectorReferenceLifetimeNativeReceipt.AssertAsync(profile, original, receipt, token);
        await LineageAsync(profile, originalSource);
        await Assert.That((await VectorReferenceLifetimeNativeCalls.SearchAsync(profile, token)).Length).IsEqualTo(OneResult);
        var drop = NativeDropPublicSubmission.Start(profile, new(NativeDropRuntimeProtocol.Tenant,
            NativeDropRuntimeProtocol.Database, NativeDropRuntimeProtocol.Table), Guid.NewGuid(), token);
        _ = NativeDropRuntimeAssertions.Value<DropTableResult>(await drop.Original);
        await NativeDropRuntimeSeed.CreateAsync(profile, token);
        var freshSource = VectorReferenceLifetimeNativeCalls.Source(profile);
        await Assert.That(freshSource.Lifetime).IsNotNull();
        await Assert.That(freshSource.Lifetime).IsNotEqualTo(originalSource.Lifetime);
        await RefuseSearchAsync(profile, token);
        await VectorReferenceLifetimeNativeCalls.EventAsync(profile, fresh: true, token);
        await RefuseWriteAsync(profile, Request(originalSource, fresh: true), token);
        var healthy = new CommandRequest(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [Request(freshSource with { Lifetime = null }, fresh: true)]);
        var repaired = await VectorReferenceLifetimeNativeCalls.CommitAsync(profile, healthy, token);
        await VectorReferenceLifetimeNativeReceipt.AssertAsync(profile, healthy, repaired, token);
        await LineageAsync(profile, freshSource);
        for (var cold = FirstCold; cold < ColdCuts; cold++)
        {
            await profile.RestartAsync(token);
            await NativeDropRuntimeAssertions.SameAsync(repaired,
                await VectorReferenceLifetimeNativeCalls.CommitAsync(profile, healthy, token));
            await LineageAsync(profile, freshSource);
            var found = await VectorReferenceLifetimeNativeCalls.SearchAsync(profile, token);
            await Assert.That(found.Length).IsEqualTo(OneResult);
            await Assert.That(found.Single().Document.Reference.Id).IsEqualTo(NativeDropRuntimeProtocol.Row);
        }
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
    }

    private static async Task LineageAsync(NativeDropRuntimeProfile profile, EntityRef expected)
    {
        var key = VectorProjectionKeys.Lineage(NativeDropRuntimeProtocol.Partition, Target, Field, NativeDropRuntimeProtocol.Row);
        var lineage = profile.Source.Store.Read(view => view.GetRecord<VectorProjectionLineage>(key));
        await Assert.That(lineage).IsNotNull();
        await NativeDropRuntimeAssertions.SameAsync(expected, lineage!.SourceDocument);
    }

    private static async Task RefuseSearchAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => VectorReferenceLifetimeNativeCalls.SearchAsync(profile, token));
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }

    private static async Task RefuseWriteAsync(NativeDropRuntimeProfile profile, ApplyVectorProjection request, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var command = new CommandRequest(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition, [request]);
        var denied = await NativeDropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(command), token);
        await Assert.That(denied.Error).IsEqualTo((ErrorCode?)ErrorCode.OwnershipLost);
        await Assert.That(denied.Payload.IsEmpty).IsTrue();
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }
}
