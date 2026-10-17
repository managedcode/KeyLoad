using System.Text;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeModelWitnessCase : IDisposable
{
    private readonly ReplicaSecurityFixture security = new();
    internal RemoteDocumentMac Mac
    {
        get;
    }
    internal RequestCqrsNativeModelWitnessCase() => Mac = new(security.Options.Secret.Span);

    internal RequestCqrsNativeModelWitness Create(bool after = true)
    {
        ReadOnlyMemory<byte> payload = NativeSerialization.Serialize(Guid.NewGuid());
        var voters = security.Configuration.VoterIds;
        var owner = new PhysicalShardRecord(Guid.NewGuid(), security.Configuration.Incarnation, voters, 1);
        var request = new RemoteModelNativeRequest(RemoteModelNativeChannel.DocumentEnvelope, payload,
            Mac.Sign(payload.Span, false), owner, owner, security.Configuration.LocalId,
            security.Discovery.RuntimeAddress, security.Configuration.LocalId, security.Discovery.RuntimeAddress,
            Guid.NewGuid(), TimeProvider.System.GetUtcNow().AddSeconds(1), string.Empty);
        request = request with
        {
            SenderSignature = Mac.SignNativeRequest(request)
        };
        var reply = new RemoteModelNativeReply(payload, Mac.Sign(payload.Span, true), null, null, request.Nonce, string.Empty);
        reply = reply with
        {
            SenderSignature = Mac.SignNativeReply(reply)
        };
        return new(RequestCqrsProbeProtocol.Version, Guid.NewGuid().ToString("N"), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), after ? RequestCqrsProbePhase.NativeModelAfterDispatch
                : RequestCqrsProbePhase.NativeModelBeforeDispatch, request.CallerVoter, request.CallerSiloAddress,
            NativeSerialization.Serialize(request), after ? NativeSerialization.Serialize(reply) : null);
    }

    internal static RequestCqrsProbeMarkerRecord Marker(RequestCqrsNativeModelWitness value)
        => new(value.Version, RequestCqrsProbeProtocol.MarkerKind, value.SessionId, value.ArmId, value.RequestId,
            value.CommandId, value.Phase, RequestCqrsProbeOutcome.Observed, value.Voter, value.SiloAddress);

    internal static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);

    public void Dispose()
    {
        Mac.Dispose();
        security.Dispose();
    }
}
