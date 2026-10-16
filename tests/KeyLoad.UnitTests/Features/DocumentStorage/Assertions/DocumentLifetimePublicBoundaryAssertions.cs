using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Server.Features.DocumentStorage;
using C = KeyLoad.UnitTests.Features.DocumentStorage.RemoteDocumentNativeFixture;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentLifetimePublicBoundaryAssertions
{
    private const string OtherId = "wrong-target-entity";
    private const string OtherCollection = "wrong-target-collection";
    private const string OtherPartition = "wrong-target-partition";

    internal static async Task RequireAsync(RemoteDocumentNativeFixture fixture, RemoteDocumentReadFrameV2 frame,
        OwnedDocumentReadResultV1 result)
    {
        var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
        var target = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        var sourcePosition = fixture.Source.Store.Position;
        var targetPosition = fixture.Destination.Store.Position;
        var expected = fixture.ExpectedDestinationReference;
        await Assert.That(result.Document!.Reference).IsEqualTo(expected);
        await Assert.That(expected.Lifetime).IsNotEqualTo(frame.Reference.Lifetime);
        var request = DocumentLifetimeNativeRead.Request(fixture, frame);
        await Assert.That(request.Request.Reference).IsEqualTo(C.Reference);
        RemoteDocumentLifetimeReceiver.RequireSourceReference(request, fixture.Source.Database.Limits.MaxBatchBytes);
        EntityRef[] wrongSources = [expected, frame.Reference with { Id = OtherId },
            frame.Reference with { Collection = OtherCollection },
            frame.Reference with { Partition = frame.Reference.Partition with { PartitionKey = OtherPartition } }];
        foreach (var reference in wrongSources)
        {
            var wrongSource = request with
            {
                Request = request.Request with
                {
                    Reference = reference
                }
            };
            var refused = Assert.ThrowsExactly<KeyLoadException>(() => RemoteDocumentLifetimeReceiver.RequireSourceReference(
                wrongSource, fixture.Source.Database.Limits.MaxBatchBytes));
            await Assert.That(refused.Code).IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That(refused.Message).IsEqualTo(RemoteDocumentProtocol.InvalidProof);
        }
        EntityRef[] changed = [expected with { Id = OtherId }, expected with { Collection = OtherCollection },
            expected with { Partition = expected.Partition with { PartitionKey = OtherPartition } }];
        foreach (var reference in changed)
        {
            var reply = result with
            {
                Document = result.Document with
                {
                    Reference = reference
                }
            };
            var error = Assert.ThrowsExactly<KeyLoadException>(() => OrdinaryDocumentLifetimeSourceRead.RequireResult(reply, frame));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(error.Message).IsEqualTo(RemoteDocumentProtocol.Unavailable);
        }
        var unbound = result with
        {
            Document = result.Document with
            {
                Reference = expected with
                {
                    Lifetime = null
                }
            }
        };
        var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => OrdinaryDocumentLifetimeSourceRead.RequireResult(unbound, frame));
        await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(unsupported.Message).IsEqualTo(RemoteDocumentProtocol.Unavailable);
        OrdinaryDocumentLifetimeSourceRead.RequireResult(result, frame);
        await Assert.That(result.Document.Reference).IsEqualTo(expected);
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(source)).IsTrue();
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(target)).IsTrue();
    }
}
