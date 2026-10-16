using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ResourceLifetimeReadProbeSelectionTests
{
    [Test]
    public async Task ArmedReadSelectionIsClosedOverActualPublicReadAndModelPairs()
    {
        foreach (var read in Enum.GetValues<GrainReadKind>())
        {
            var expected = read is GrainReadKind.Document or GrainReadKind.BlobMetadata or GrainReadKind.BlobUploadInfo
                or GrainReadKind.BlobRange or GrainReadKind.BlobList or GrainReadKind.PartitionQuery or GrainReadKind.DistributedSearch
                or GrainReadKind.WaitForAnnIndex or GrainReadKind.ApproximateSearch;
            await Assert.That(RequestCqrsLifetimeReadProbe.IsReadKind(read)).IsEqualTo(expected);
            foreach (var kind in Enum.GetValues<RemoteResourceLifetimeReadKindV2>())
            {
                var accepted = RequestCqrsLifetimeReadProbe.Matches(kind, read);
                if (!expected)
                {
                    await Assert.That(accepted).IsFalse();
                }
            }
        }
        await Assert.That(RequestCqrsLifetimeReadProbe.Matches(RemoteResourceLifetimeReadKindV2.Document, GrainReadKind.BlobRange)).IsFalse();
        await Assert.That(RequestCqrsLifetimeReadProbe.Matches(RemoteResourceLifetimeReadKindV2.DistributedSearch, GrainReadKind.PartitionQuery)).IsFalse();
        await Assert.That(RequestCqrsLifetimeReadProbe.Matches((RemoteResourceLifetimeReadKindV2)int.MaxValue, GrainReadKind.Document)).IsFalse();
        await Assert.That(RequestCqrsLifetimeReadProbe.IsReadKind(null)).IsFalse();
        foreach (var read in new[] { GrainReadKind.WaitForAnnIndex, GrainReadKind.ApproximateSearch })
        {
            await Assert.That(RequestCqrsLifetimeReadProbe.ValidArm(RequestCqrsProbePhase.NativeModelAfterDispatch,
                RequestCqrsProbeAction.Hold, read, Guid.Empty)).IsTrue();
            await Assert.That(RequestCqrsLifetimeReadProbe.ValidArm(RequestCqrsProbePhase.NativeModelBeforeDispatch,
                RequestCqrsProbeAction.Hold, read, Guid.Empty)).IsFalse();
        }
    }

    [Test]
    public void EmptyCommandWitnessDoesNotAuthorizeOldNativeChannelsOrBeforeDispatch()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var original = fixture.Create();
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(original with { CommandId = Guid.Empty }));
        var native = RequestCqrsNativeModelInventory.Request(original);
        var call = new RemoteResourceLifetimeReadCallV2(RemoteResourceLifetimeReadProtocol.Version,
            RemoteResourceLifetimeReadKindV2.Document, original.RequestId, Guid.NewGuid().ToString("N"), native.ExpiresAt,
            native.CallerVoter, native.CallerSiloAddress, native.SourceOwner,
            new(native.DestinationOwner, []), native.Payload, RemoteDocumentProtocol.MaximumBodyBytes);
        var payload = NativeSerialization.Serialize(call);
        var request = native with
        {
            Channel = RemoteModelNativeChannel.ResourceLifetimeRead,
            Payload = payload,
            Signature = fixture.Mac.Sign(payload, reply: false),
            SenderSignature = string.Empty
        };
        request = request with
        {
            SenderSignature = fixture.Mac.SignNativeRequest(request)
        };
        var after = original with
        {
            CommandId = Guid.Empty,
            NativeRequest = NativeSerialization.Serialize(request)
        };
        _ = RequestCqrsNativeModelInventory.Write(after);
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(after with
        {
            Phase = RequestCqrsProbePhase.NativeModelBeforeDispatch,
            NativeReply = null
        }));
        _ = RequestCqrsNativeModelInventory.Write(original);
    }
}
