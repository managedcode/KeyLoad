using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementNativeEnvelopeFlow
{
    internal static async Task CorrelationAsync()
    {
        var clock = new ControlledReadClock(TimeProvider.System.GetUtcNow());
        using var fixture = new ReplicaSecurityFixture(clock: clock);
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, ResourceRetirementNativeEnvelopeData.Limits.Value.MaxBatchBytes);
        var first = ResourceRetirementNativeEnvelopeData.Sign(mac, ResourceRetirementNativeEnvelopeData.Request(fixture, clock.GetUtcNow()));
        var next = ResourceRetirementNativeEnvelopeData.Sign(mac, first with
        {
            RequestId = Guid.NewGuid(),
            Nonce = Guid.NewGuid()
        });
        await Assert.That(next.RequestId).IsNotEqualTo(first.RequestId);
        await Assert.That(next.CommandId).IsEqualTo(first.CommandId);
        await Assert.That(next.Signature).IsNotEqualTo(first.Signature);
        foreach (var original in new[] { first, next })
        {
            var bytes = NativeSerialization.Serialize(original);
            var restored = NativeSerialization.Deserialize<ResourceRetirementNativeRequest>(bytes);
            await Assert.That(NativeSerialization.Serialize(restored).AsSpan().SequenceEqual(bytes)).IsTrue();
            ResourceRetirementNativeEnvelopeData.RequireRequest(mac, restored, clock);
            var reply = ResourceRetirementNativeEnvelopeData.Reply(mac, restored);
            var restoredReply = NativeSerialization.Deserialize<ResourceRetirementNativeReply>(NativeSerialization.Serialize(reply));
            await Assert.That(restoredReply.RequestSignature).IsEqualTo(original.Signature);
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, restored, restoredReply, clock);
        }
        var oldReply = ResourceRetirementNativeEnvelopeData.Reply(mac, first);
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, next, oldReply, clock), ErrorCode.Unauthenticated);
        var changed = ResourceRetirementNativeEnvelopeData.Reply(mac, next) with
        {
            RequestSignature = first.Signature
        };
        changed = changed with
        {
            Signature = ResourceRetirementMac.SignReply(mac, changed, ResourceRetirementNativeEnvelopeData.Limits)
        };
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, next, changed, clock), ErrorCode.Unauthenticated);
        ResourceRetirementNativeEnvelopeData.RequireReply(mac, next, ResourceRetirementNativeEnvelopeData.Reply(mac, next), clock);
    }

    internal static async Task TamperingAsync()
    {
        var clock = new ControlledReadClock(TimeProvider.System.GetUtcNow());
        using var fixture = new ReplicaSecurityFixture(clock: clock);
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, ResourceRetirementNativeEnvelopeData.Limits.Value.MaxBatchBytes);
        var request = ResourceRetirementNativeEnvelopeData.Sign(mac, ResourceRetirementNativeEnvelopeData.Request(fixture, clock.GetUtcNow()));
        var changed = new[]
        {
            request with { CommandId = Guid.NewGuid() },
            request with { Command = false, CommandId = Guid.Empty },
            request with { Payload = NativeSerialization.Serialize(Guid.NewGuid()) },
            request with { Destination = request.Destination with { Owner = request.Destination.Owner with { Incarnation = Guid.NewGuid() } } }
        };
        foreach (var mutation in changed)
        {
            await Assert.That(ResourceRetirementMac.VerifyRequest(mac, mutation, ResourceRetirementNativeEnvelopeData.Limits)).IsFalse();
            await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
                ResourceRetirementNativeEnvelopeData.RequireRequest(mac, mutation, clock), ErrorCode.Unauthenticated);
        }
        var query = ResourceRetirementNativeEnvelopeData.Sign(mac, changed[1]);
        ResourceRetirementNativeEnvelopeData.RequireRequest(mac, query, clock);
        foreach (var malformed in new[] { request with { CommandId = Guid.Empty }, query with { CommandId = request.CommandId } })
        {
            var signed = ResourceRetirementNativeEnvelopeData.Sign(mac, malformed);
            await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
                ResourceRetirementNativeEnvelopeData.RequireRequest(mac, signed, clock), ErrorCode.Unauthenticated);
        }
        ResourceRetirementNativeEnvelopeData.RequireRequest(mac, request, clock);
        ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, ResourceRetirementNativeEnvelopeData.Reply(mac, request), clock);
    }

    internal static async Task DomainsAsync()
    {
        var clock = new ControlledReadClock(TimeProvider.System.GetUtcNow());
        using var fixture = new ReplicaSecurityFixture(clock: clock);
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, ResourceRetirementNativeEnvelopeData.Limits.Value.MaxBatchBytes);
        var request = ResourceRetirementNativeEnvelopeData.Sign(mac, ResourceRetirementNativeEnvelopeData.Request(fixture, clock.GetUtcNow()));
        var reply = ResourceRetirementNativeEnvelopeData.Reply(mac, request);
        var requestBytes = NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        });
        var replyBytes = NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        });
        await Assert.That(request.Signature).IsEqualTo(RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            requestBytes, ResourceRetirementNativeEnvelopeData.RequestPurpose));
        await Assert.That(reply.Signature).IsEqualTo(RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            replyBytes, ResourceRetirementNativeEnvelopeData.ReplyPurpose));
        foreach (var purpose in new[] { ResourceRetirementNativeEnvelopeData.ReplyPurpose, ResourceRetirementNativeEnvelopeData.InventoryPurpose })
        {
            var wrong = request with
            {
                Signature = RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span, requestBytes, purpose)
            };
            await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
                ResourceRetirementNativeEnvelopeData.RequireRequest(mac, wrong, clock), ErrorCode.Unauthenticated);
        }
        var wrongReply = reply with
        {
            Signature = RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            replyBytes, ResourceRetirementNativeEnvelopeData.RequestPurpose)
        };
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, wrongReply, clock), ErrorCode.Unauthenticated);
        ResourceRetirementNativeEnvelopeData.RequireRequest(mac, request, clock);
        ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, reply, clock);
    }
}
