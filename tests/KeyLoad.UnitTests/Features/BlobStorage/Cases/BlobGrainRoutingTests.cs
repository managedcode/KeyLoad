using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>AC-BLOB-003/006: real signed codec preserves canonical commands and fresh request identities.</summary>
internal sealed class BlobGrainRoutingTests
{
    private const string OtherDomain = "different-domain";
    private const string OtherPartition = "different-partition";

    /// <summary>All six commands retain a stable write ID across independent signed request actors and route full atomic scope.</summary>
    [Test]
    public async Task AcBlob006FreshSignedActorsPreserveEachStableCommandAndAtomicPartition()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        foreach (var item in BlobAgentCases.All().Where(item => item.CommandKind.HasValue))
        {
            var first = codec.Verify(Sign(codec, item, Guid.NewGuid()));
            var second = codec.Verify(Sign(codec, item, Guid.NewGuid()));
            await Assert.That(first.Envelope.RequestId).IsNotEqualTo(second.Envelope.RequestId);
            await Assert.That(first.Envelope.CommandId).IsEqualTo(BlobAgentCases.CommandId);
            await Assert.That(second.Envelope.CommandId).IsEqualTo(BlobAgentCases.CommandId);
            await Assert.That(first.Payload.Span.SequenceEqual(item.NativePayload.Span)).IsTrue();
            await Assert.That(GrainPartitionResolver.Resolve(first)).IsEqualTo(BlobAgentCases.Partition.AtomicPartitionId);
        }
    }

    /// <summary>Every blob read carries exact bytes and cannot be used under a different actor identity.</summary>
    [Test]
    public async Task AcBlob006SignedReadActorsRequireTheirFreshRequestIdentity()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        foreach (var item in BlobAgentCases.All().Where(item => item.ReadKind.HasValue))
        {
            var id = Guid.NewGuid();
            var token = codec.CreateRead(id, BlobAgentCases.Principal, item.ReadKind!.Value, item.NativePayload);
            var decoded = codec.VerifyRead(token, id);
            await Assert.That(decoded.Payload.Span.SequenceEqual(item.NativePayload.Span)).IsTrue();
            await Assert.That(decoded.Envelope.CommandId).IsEqualTo(Guid.Empty);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(token, Guid.NewGuid())).Code)
                .IsEqualTo(ErrorCode.TokenInvalidated);
        }
    }

    /// <summary>Genuinely signed mismatched DTO IDs and wrong domain/partition actor keys are rejected before effects.</summary>
    [Test]
    public async Task AcBlob003WrongCommandIdentityOrAtomicActorCannotExecuteAnyBlobCommand()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var executor = new GrainCommandExecutor(fixture.Database, new EmbeddedCoordinator(fixture.Database), TimeProvider.System);
        var keys = new[]
        {
            (BlobAgentCases.Partition with { TransactionDomainId = OtherDomain }).AtomicPartitionId,
            (BlobAgentCases.Partition with { PartitionKey = OtherPartition }).AtomicPartitionId
        };
        foreach (var item in BlobAgentCases.All().Where(item => item.CommandKind.HasValue))
        {
            var badId = codec.Verify(codec.CreateCommand(Guid.NewGuid(), BlobAgentCases.Principal,
                item.CommandKind!.Value, Guid.NewGuid(), item.NativePayload));
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPartitionResolver.Resolve(badId)).Code)
                .IsEqualTo(ErrorCode.TokenInvalidated);
            var valid = codec.Verify(Sign(codec, item, Guid.NewGuid()));
            foreach (var key in keys)
            {
                var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => SignedGrainRequestTestContext.ExecuteAsync(executor, valid, key,
                    TestContext.Current!.Execution.CancellationToken)) ?? throw new InvalidOperationException();
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            }
            await Assert.That(fixture.Database.Outcome(BlobAgentCases.Principal, BlobAgentCases.CommandId)).IsNull();
        }
    }

    private static string Sign(GrainRequestCodec codec, BlobAgentCase item, Guid requestId) =>
        codec.CreateCommand(requestId, BlobAgentCases.Principal, item.CommandKind!.Value, BlobAgentCases.CommandId, item.NativePayload);
}
