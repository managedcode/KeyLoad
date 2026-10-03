using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaSenderValidator
{
    internal static void Validate(ReplicaRpc method, ReadOnlyMemory<byte> payload, string sender, ReplicaConfiguration configuration)
    {
        switch (method)
        {
            case ReplicaRpc.RequestVote:
                _ = ReplicaNativeAdmissionPolicy.Inspect<VoteRequest>(payload, configuration.MaxAppendEntries, sender);
                break;
            case ReplicaRpc.Append:
            case ReplicaRpc.ReadProbe:
                _ = ReplicaNativeAdmissionPolicy.Inspect<AppendRequest>(payload, configuration.MaxAppendEntries, sender);
                break;
            case ReplicaRpc.SnapshotBegin:
                _ = ReplicaNativeAdmissionPolicy.Inspect<SnapshotBeginRequest>(payload, configuration.MaxAppendEntries, sender);
                break;
            case ReplicaRpc.SnapshotChunk:
                _ = ReplicaNativeAdmissionPolicy.Inspect<SnapshotChunkRequest>(payload, configuration.MaxAppendEntries, sender);
                break;
            case ReplicaRpc.SnapshotComplete:
                _ = ReplicaNativeAdmissionPolicy.Inspect<SnapshotCompleteRequest>(payload, configuration.MaxAppendEntries, sender);
                break;
            case ReplicaRpc.Forward:
            case ReplicaRpc.ReadBarrier:
            case ReplicaRpc.ControlReadBarrier:
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }
}
