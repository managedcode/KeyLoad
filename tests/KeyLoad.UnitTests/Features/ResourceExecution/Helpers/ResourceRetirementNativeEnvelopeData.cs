using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementNativeEnvelopeData
{
    internal const string RequestPurpose = "keyload-resource-retirement-native-request-v1";
    internal const string ReplyPurpose = "keyload-resource-retirement-native-reply-v1";
    internal const string InventoryPurpose = "keyload-resource-owner-inventory-native-request-v1";
    internal const string InvalidProof = "The resource retirement envelope is invalid.";
    private const string Principal = "root";
    internal static IOptions<DatabaseLimits> Limits { get; } = UnitExecutionOptions.DatabaseLimits();
    internal static IOptions<GrainRoutingOptions> Routing { get; } = UnitRoutingOptions.Routing();
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(1);

    internal static ResourceRetirementNativeRequest Request(ReplicaSecurityFixture fixture, DateTimeOffset now)
    {
        var owners = PhysicalOwnerProbeNativeTestValues.Call(fixture);
        var endpoints = fixture.Configuration.VoterIds.Select(static endpoint => new Uri(endpoint).AbsoluteUri);
        var control = owners.Control with
        {
            Endpoints = [.. endpoints]
        };
        var destination = owners.Destination with
        {
            Endpoints = [.. endpoints]
        };
        var commandId = Guid.NewGuid();
        return new(ResourceRetirementNativeProtocol.Version, Guid.NewGuid(), Guid.NewGuid(), Principal, 0,
            control, destination, ReplicaSecurityFixture.VoterA, fixture.Discovery.RuntimeAddress,
            ReplicaSecurityFixture.VoterB, fixture.Discovery.RuntimeAddress, now, now + Lifetime,
            Limits.Value.MaxQueryReadBytes, Limits.Value.MaxScanRecords, Limits.Value.MaxBatchBytes,
            NativeSerialization.Serialize(commandId), string.Empty, commandId, true);
    }

    internal static ResourceRetirementNativeRequest Sign(PartitionMovementMac mac, ResourceRetirementNativeRequest request)
        => request with
        {
            Signature = ResourceRetirementMac.SignRequest(mac, request, Limits)
        };

    internal static ResourceRetirementNativeReply Reply(PartitionMovementMac mac, ResourceRetirementNativeRequest request)
    {
        var reply = new ResourceRetirementNativeReply(request.Version, request.RequestId, request.Nonce,
            request.Signature, NativeSerialization.Serialize(request.CommandId), null, null, string.Empty);
        return reply with
        {
            Signature = ResourceRetirementMac.SignReply(mac, reply, Limits)
        };
    }

    internal static string ChangeSignature(string signature)
        => (signature[0] == '0' ? '1' : '0') + signature[1..];

    internal static async Task RefusalAsync(Action operation, ErrorCode code)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(error.Message).IsEqualTo(InvalidProof);
    }

    internal static void RequireRequest(PartitionMovementMac mac, ResourceRetirementNativeRequest request, TimeProvider clock)
        => ResourceRetirementEnvelopeValidation.RequireRequest(mac, request, Limits, Routing, clock);

    internal static void RequireReply(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        ResourceRetirementNativeReply reply, TimeProvider clock)
        => ResourceRetirementEnvelopeValidation.RequireReply(mac, request, reply, Limits, clock);
}
