using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

// Native codec/MAC inputs with an actual persisted intent; this does not simulate a successful RPC.
internal sealed class RequestCqrsNativeReplayFrameCase : IDisposable
{
    private readonly ReplicaSecurityFixture security = new();
    private readonly RemoteTransferDatabase database = new();
    internal const string Principal = "c1-probe-replay-codec";
    internal RemoteDocumentMac Mac { get; }
    internal RequestCqrsNativeReplayFrameCase()
    {
        Mac = new(security.Options.Secret.Span);
        database.AddPrincipal(new(Principal, RemoteTransferDatabase.TenantId, [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true });
    }

    internal RequestCqrsNativeReplayWitness Create(RemoteModelNativeReplayVariation mode = RemoteModelNativeReplayVariation.Original2)
    {
        var transferId = Guid.NewGuid();
        var create = new CommandRequest(Guid.NewGuid(), database.SourcePartition,
            [new CreateQueueTransfer(database.SourceQueue, transferId, database.DestinationQueue,
                new(database.DestinationQueue.Queue, "codec-" + transferId.ToString("N"), "{}"))]);
        var receipt = database.Apply(OperationKind.Batch, create, principal: Principal).Get<CommitReceipt>();
        var intent = database.Database.InspectQueueTransfer(Principal, database.SourceQueue, transferId)!;
        var claims = database.Database.Verify<RemoteTransferIntentClaims>(intent.IntentToken);
        var owner = new PhysicalShardRecord(Guid.NewGuid(), security.Configuration.Incarnation, security.Configuration.VoterIds, 1);
        var registered = new RegisteredPhysicalOwnerV1(owner, security.Configuration.VoterIds);
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var expiry = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var call = new RemoteQueueTransferPeerCall(RemoteTransferPeerProtocol.Version, requestId, Guid.NewGuid().ToString("N"),
            expiry, security.Configuration.LocalId, security.Discovery.RuntimeAddress, registered, registered,
            RemoteQueueTransferPeerStage.Accept, commandId, claims.PrincipalId, 1, claims.Fingerprint,
            intent.IntentToken, claims, RemoteDocumentProtocol.MaximumBodyBytes, receipt.Token);
        var payload = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, null, QueueTransfer: call));
        var original = new RemoteModelNativeRequest(RemoteModelNativeChannel.DocumentEnvelope, payload, Mac.Sign(payload.AsSpan(), false),
            owner, owner, security.Configuration.LocalId, security.Discovery.RuntimeAddress, security.Configuration.LocalId,
            security.Discovery.RuntimeAddress, Guid.NewGuid(), expiry, string.Empty);
        original = original with { SenderSignature = Mac.SignNativeRequest(original) };
        var actual = original with { Payload = RemoteModelNativeReplayPayload.Create(original, mode), Nonce = Guid.NewGuid(), SenderSignature = string.Empty };
        actual = actual with { SenderSignature = Mac.SignNativeRequest(actual) };
        var refusal = new RemoteModelNativeReply(ReadOnlyMemory<byte>.Empty, string.Empty, ErrorCode.Unauthenticated, null, actual.Nonce, string.Empty);
        refusal = refusal with { SenderSignature = Mac.SignNativeReply(refusal) };
        return new(RequestCqrsProbeProtocol.Version, Guid.NewGuid().ToString("N"), Guid.NewGuid(), requestId, commandId,
            Guid.NewGuid(), actual.CallerVoter, actual.CallerSiloAddress, mode, 1,
            NativeSerialization.Serialize(original), NativeSerialization.Serialize(actual), NativeSerialization.Serialize(refusal));
    }

    public void Dispose() { Mac.Dispose(); database.Dispose(); security.Dispose(); }
}
