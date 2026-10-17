using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class D3PrivateRetirementCodecAssertions
{
    private const string Root = "root";
    private const string ReadPurpose = "keyload.resource-retirement.read.v1";
    private const string CommandPurpose = "keyload.resource-retirement.command.v1";

    internal static async Task RefusalsAsync(ControlledPartitionMovementNode source, GrainRequestCodec codec,
        ResourceRetirementReadRequestV1 query, ReplicatedOperation operation, DateTimeOffset expiry,
        NativeCommandMetadataAdmissionV1 metadata)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var index = source.Journal.Log.State.LastIndex;
        var payload = NativeSerialization.Serialize(query);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), Root,
            GrainReadKind.ResourceRetirement, payload)).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(Guid.NewGuid(), Root,
            OperationKind.ResourceRetirementPhase, operation.Id, operation.NativePayload)).Code).IsEqualTo(ErrorCode.PermissionDenied);
        var id = Guid.NewGuid();
        var signed = codec.CreateResourceRetirementRead(id, Root, payload, expiry);
        var original = codec.VerifyRead(signed, id);
        await Assert.That(original.Envelope.Purpose).IsEqualTo(ReadPurpose);
        await Assert.That(original.Envelope.ExpiresAt).IsEqualTo(expiry);
        await Assert.That(original.Payload.Span.SequenceEqual(payload)).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(signed, Guid.NewGuid())).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        GrainRequestEnvelope[] changed =
        [
            original.Envelope with { Purpose = CommandPurpose },
            original.Envelope with { CommandId = Guid.NewGuid() },
            original.Envelope with { ExpiresAt = DateTimeOffset.UnixEpoch },
            original.Envelope with { Incarnation = Guid.NewGuid() }
        ];
        foreach (var envelope in changed)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(source.Database.Sign(envelope), id)).Code)
                .IsEqualTo(ErrorCode.TokenInvalidated);
            await UnchangedAsync(source, before, index);
            await Assert.That(codec.VerifyRead(signed, id).Payload.Span.SequenceEqual(payload)).IsTrue();
        }
        var command = codec.Verify(codec.CreateResourceRetirementCommand(Guid.NewGuid(), operation, expiry, metadata));
        await Assert.That(command.Envelope.Purpose).IsEqualTo(CommandPurpose);
        await Assert.That(command.Envelope.CommandId).IsEqualTo(operation.Id);
        await Assert.That(command.Envelope.ExpiresAt).IsEqualTo(expiry);
        var wrong = codec.Verify(source.Database.Sign(command.Envelope with
        {
            CommandId = Guid.NewGuid()
        }));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPartitionResolver.Resolve(wrong)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateResourceRetirementCommand(Guid.NewGuid(),
            operation with
            {
                Id = Guid.NewGuid()
            }, expiry, metadata)).Code).IsEqualTo(ErrorCode.Corruption);
        await UnchangedAsync(source, before, index);
    }

    private static async Task UnchangedAsync(ControlledPartitionMovementNode source, string[] before, long index)
    {
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
    }
}
