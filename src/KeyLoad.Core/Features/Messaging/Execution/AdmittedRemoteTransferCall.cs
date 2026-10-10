namespace KeyLoad.Core.Features.Messaging;

internal sealed class AdmittedRemoteTransferCall
{
    private AdmittedRemoteTransferCall(RemoteTransferNativeProof proof) => Proof = proof;
    internal RemoteTransferNativeProof Proof { get; }

    internal static AdmittedRemoteTransferCall Admit(DatabaseEngine database, ReadOnlyMemory<byte> originalEnvelope,
        string originalSignature, RemoteQueueTransferPeerCall call, ReadExecutionBudget work)
        => new(database.AdmitRemoteTransferPeerCall(originalEnvelope, originalSignature, call, work));
}
