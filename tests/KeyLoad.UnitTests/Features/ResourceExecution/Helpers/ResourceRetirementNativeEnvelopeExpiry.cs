using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementNativeEnvelopeExpiry
{
    internal static async Task RunAsync()
    {
        var clock = new ControlledReadClock(TimeProvider.System.GetUtcNow());
        using var fixture = new ReplicaSecurityFixture(clock: clock);
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, ResourceRetirementNativeEnvelopeData.Limits.Value.MaxBatchBytes);
        var request = ResourceRetirementNativeEnvelopeData.Sign(mac, ResourceRetirementNativeEnvelopeData.Request(fixture, clock.GetUtcNow()));
        var reply = ResourceRetirementNativeEnvelopeData.Reply(mac, request);
        ResourceRetirementNativeEnvelopeData.RequireRequest(mac, request, clock);
        ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, reply, clock);
        clock.Advance(ResourceRetirementNativeEnvelopeData.Lifetime + TimeSpan.FromTicks(1));
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireRequest(mac, request, clock), ErrorCode.CursorExpired);
        var invalidRequest = request with
        {
            Signature = ResourceRetirementNativeEnvelopeData.ChangeSignature(request.Signature)
        };
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireRequest(mac, invalidRequest, clock), ErrorCode.Unauthenticated);
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireRequest(mac, request with
            {
                Signature = string.Empty
            }, clock), ErrorCode.Unauthenticated);
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, reply, clock), ErrorCode.CursorExpired);
        var invalidReply = reply with
        {
            Signature = ResourceRetirementNativeEnvelopeData.ChangeSignature(reply.Signature)
        };
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, invalidReply, clock), ErrorCode.Unauthenticated);
        await ResourceRetirementNativeEnvelopeData.RefusalAsync(() =>
            ResourceRetirementNativeEnvelopeData.RequireReply(mac, request, reply with
            {
                Signature = string.Empty
            }, clock), ErrorCode.Unauthenticated);
        var fresh = ResourceRetirementNativeEnvelopeData.Sign(mac, ResourceRetirementNativeEnvelopeData.Request(fixture, clock.GetUtcNow()));
        ResourceRetirementNativeEnvelopeData.RequireRequest(mac, fresh, clock);
        ResourceRetirementNativeEnvelopeData.RequireReply(mac, fresh, ResourceRetirementNativeEnvelopeData.Reply(mac, fresh), clock);
    }
}
