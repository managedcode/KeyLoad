namespace KeyLoad.Replication;

internal static class ReplicaReadRoundGuard
{
    private const int EmptyEntryCount = 0;
    private const int FirstElectionTerm = 1;
    private const int BeforeFirstLogPosition = 0;
    private const int UnelectedTerm = 0;
    private const int EmptyReadRequestLength = 0;
    private const int NoEntryPayloads = 0;

    internal static AppendRequest Probe(ReadOnlyMemory<byte> bytes, ReplicaConfiguration configuration)
    {
        if (bytes.Length > (long)configuration.MaxAppendBytes + ReplicaProtocol.PayloadMetadataBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var request = Read<AppendRequest>(bytes);
        if (request.Entries.IsDefault || request.Entries.Length != EmptyEntryCount || request.Term < FirstElectionTerm
            || request.PreviousIndex < BeforeFirstLogPosition || request.PreviousIndex == long.MaxValue
            || request.PreviousTerm < UnelectedTerm || request.PreviousTerm > request.Term || request.CommittedIndex < BeforeFirstLogPosition
            || (request.PreviousIndex == BeforeFirstLogPosition) != (request.PreviousTerm == UnelectedTerm)
            || !configuration.VoterIds.Contains(request.LeaderId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        return request;
    }

    internal static void Empty(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ReplicaProtocol.PayloadMetadataBytes || Read<string>(bytes).Length != EmptyReadRequestLength)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
    }

    private static T Read<T>(ReadOnlyMemory<byte> bytes) where T : class
    {
        try
        {
            return ReplicaProtocolCodec.DeserializeStored<T>(bytes, maximumEntries: NoEntryPayloads);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
    }
}
