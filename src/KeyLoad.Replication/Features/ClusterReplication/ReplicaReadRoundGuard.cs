namespace KeyLoad.Replication;

internal static class ReplicaReadRoundGuard
{
    internal static AppendRequest Probe(ReadOnlyMemory<byte> bytes, ReplicaConfiguration configuration)
    {
        if (bytes.Length > (long)configuration.MaxAppendBytes + ReplicaProtocol.PayloadMetadataBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var request = Read<AppendRequest>(bytes);
        if (request.Entries.IsDefault || request.Entries.Length != 0 || request.Term < 1
            || request.PreviousIndex < 0 || request.PreviousIndex == long.MaxValue
            || request.PreviousTerm < 0 || request.PreviousTerm > request.Term || request.CommittedIndex < 0
            || (request.PreviousIndex == 0) != (request.PreviousTerm == 0)
            || !configuration.VoterIds.Contains(request.LeaderId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        return request;
    }

    internal static void Empty(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ReplicaProtocol.PayloadMetadataBytes || Read<string>(bytes).Length != 0)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
    }

    private static T Read<T>(ReadOnlyMemory<byte> bytes) where T : class
    {
        try
        {
            return ReplicaProtocolCodec.DeserializeStored<T>(bytes, maximumEntries: 0);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
    }
}
