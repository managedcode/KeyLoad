using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledMovementOwnerAdmissionAssertions
{
    internal static async Task<DatabaseEngine> RejectAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest request, CancellationToken token)
    {
        var image = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var key = Convert.FromBase64String(runtime.Node.Value.PeerSecret);
        var failures = new List<Exception>();
        DatabaseEngine? retained = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var mac = new PartitionMovementMac(key, source.Database.Limits.MaxBatchBytes);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var bytes = NativeSerialization.Serialize(request with
                    { Envelope = request.Envelope with { Nonce = Guid.NewGuid() } });
                    var verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), token);
                    var engine = retained = ControlledMovementUnconfiguredEngine.Create(source.Store);
                    var issue = Assert.ThrowsExactly<KeyLoadException>(() => engine.CreateVerifiedPartitionMovementOperation(
                        verified.CommandId, PhysicalShardCatalogFixture.RootPrincipalId,
                        source.Database.EvaluationClock.GetUtcNow(), verified.Envelope));
                    await Assert.That(issue.Code).IsEqualTo(ErrorCode.OwnershipLost);
                    var operation = source.Database.CreateVerifiedPartitionMovementOperation(verified.CommandId,
                        PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(), verified.Envelope);
                    var apply = Assert.ThrowsExactly<KeyLoadException>(() => engine.Apply(operation));
                    await Assert.That(apply.Code).IsEqualTo(ErrorCode.OwnershipLost);
                }, failures);
            }, failures);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(image)).IsTrue();
        return retained ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
    }

    internal static async Task RejectRetainedReadAsync(DatabaseEngine retained, ControlledPartitionMovementNode source)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => source.Store.Read(view => retained.Resource(view,
            ControlledPartitionMovementCorpus.Partition, ControlledPartitionMovementCorpus.Collection, ResourceKind.Collection)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task RejectRestorationAsync(ControlledPartitionMovementNode source)
    {
        var image = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ControlledMovementUnconfiguredEngine.Create(source.Store));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(image)).IsTrue();
    }
}
