using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class RemoteGraphEnvelopeTests
{
    [Test]
    [Arguments(GrainReadKind.RemoteGraphTargetPreparation)]
    [Arguments(GrainReadKind.RemoteGraphSourceIntent)]
    [Arguments(GrainReadKind.RemoteGraphReceiverReceipt)]
    [Arguments(GrainReadKind.RemoteGraphIncoming)]
    [Arguments(GrainReadKind.RemoteGraphReceiverCandidates)]
    [Arguments(GrainReadKind.RemoteGraphIncomingFinalize)]
    [Arguments(GrainReadKind.RemoteGraphIncomingParent)]
    public Task DedicatedReadScopePreservesNativeIdentityAndGenericReadCannotPromoteIt(GrainReadKind kind)
        => RemoteGraphNativeProofFlow.RunAsync(async database =>
        {
            var codec = RemoteGraphEnvelopeFlow.Create(database);
            var payload = RemoteGraphEnvelopeFlow.ReadPayload(database);
            var id = Guid.NewGuid();
            var expiry = TimeProvider.System.GetUtcNow() + UnitRoutingOptions.Routing().Value.RequestLifetime / 2;
            var parent = kind == GrainReadKind.RemoteGraphIncomingParent;
            var signed = parent ? codec.RemoteGraph.CreateRemoteGraphParentRead(id, "root", payload, expiry)
                : codec.RemoteGraph.CreateRemoteGraphRead(id, "root", kind, payload, expiry);
            var request = codec.VerifyRequest(signed, id);
            var before = RemoteGraphNativeProofFlow.Image(database);
            await RemoteGraphEnvelopeFlow.RequireEnvelopeAsync(database, request.Envelope, id,
                parent ? RemoteGraphRequestScope.ParentPurpose : RemoteGraphRequestScope.ReadPurpose, expiry, payload);
            await Assert.That(request.Envelope.ReadKind).IsEqualTo(kind);
            await Assert.That(request.Envelope.CommandKind).IsNull();
            await Assert.That(request.Envelope.CommandId).IsEqualTo(Guid.Empty);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(id, "root", kind, payload));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo("Cluster administration is required.");
            await RemoteGraphEnvelopeFlow.RequireRefusalAsync(database, codec, request.Envelope, before);
            await RemoteGraphEnvelopeFlow.HealthyAsync(database, codec);
        });

    [Test]
    [Arguments(OperationKind.UpsertRemoteGraphEdge)]
    [Arguments(OperationKind.DeleteRemoteGraphEdge)]
    [Arguments(OperationKind.DeliverRemoteGraphEdge)]
    public Task DedicatedParentCommandPreservesOriginalIdAndGenericCommandCannotPromoteIt(OperationKind kind)
        => RemoteGraphNativeProofFlow.RunAsync(async database =>
        {
            var codec = RemoteGraphEnvelopeFlow.Create(database);
            var (commandId, payload) = RemoteGraphEnvelopeFlow.CommandPayload(database, kind);
            var id = Guid.NewGuid();
            var expiry = TimeProvider.System.GetUtcNow() + UnitRoutingOptions.Routing().Value.RequestLifetime / 2;
            var request = codec.VerifyRequest(codec.RemoteGraph.CreateRemoteGraphParentCommand(id, "root", kind, commandId, payload, expiry), id);
            var before = RemoteGraphNativeProofFlow.Image(database);
            await RemoteGraphEnvelopeFlow.RequireEnvelopeAsync(database, request.Envelope, id,
                RemoteGraphRequestScope.ParentPurpose, expiry, payload);
            await Assert.That(request.Envelope.CommandKind).IsEqualTo(kind);
            await Assert.That(request.Envelope.CommandId).IsEqualTo(commandId);
            await Assert.That(request.Envelope.ReadKind).IsNull();
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(id, "root", kind, commandId, payload));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo("Cluster administration is required.");
            await RemoteGraphEnvelopeFlow.RequireRefusalAsync(database, codec, request.Envelope, before);
            await RemoteGraphEnvelopeFlow.HealthyAsync(database, codec);
        });

    [Test]
    public Task OriginalSignedGraphEnvelopeRejectsWrongIncarnationExpiredScopeAndDifferentActorWithoutEffects()
        => RemoteGraphNativeProofFlow.RunAsync(async database =>
        {
            var codec = RemoteGraphEnvelopeFlow.Create(database);
            var payload = RemoteGraphEnvelopeFlow.ReadPayload(database);
            var id = Guid.NewGuid();
            var expiry = TimeProvider.System.GetUtcNow() + UnitRoutingOptions.Routing().Value.RequestLifetime / 2;
            var signed = codec.RemoteGraph.CreateRemoteGraphParentRead(id, "root", payload, expiry);
            var actual = codec.VerifyRequest(signed, id);
            var before = RemoteGraphNativeProofFlow.Image(database);
            foreach (var changed in new[] { actual.Envelope with { Incarnation = Guid.NewGuid() },
                actual.Envelope with { ExpiresAt = TimeProvider.System.GetUtcNow().AddTicks(-1) } })
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRequest(database.Database.Sign(changed), id));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
                await Assert.That(RemoteGraphNativeProofFlow.Image(database)).IsEqualTo(before);
            }
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRequest(signed, Guid.NewGuid())).Code)
                .IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(RemoteGraphNativeProofFlow.Image(database)).IsEqualTo(before);
            await RemoteGraphEnvelopeFlow.HealthyAsync(database, codec);
        });
}
