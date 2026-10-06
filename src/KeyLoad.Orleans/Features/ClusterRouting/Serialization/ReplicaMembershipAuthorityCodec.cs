namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityCodec
{
    internal static byte[] SerializeCall(ReplicaMembershipAuthorityCallV1 call)
    {
        ReplicaMembershipAuthorityValidation.Call(call);
        return Serialize(call, ReplicaMembershipAuthorityProtocol.MaximumRequestBytes);
    }

    internal static byte[] SerializeReply(ReplicaMembershipAuthorityReplyV1 reply)
    {
        ReplicaMembershipAuthorityValidation.Reply(reply);
        return Serialize(reply, ReplicaMembershipAuthorityProtocol.MaximumReplyBytes);
    }

    internal static ReplicaMembershipAuthorityCallV1 DeserializeCall(ReadOnlySpan<byte> bytes)
    {
        const string DeserializeCallFailureMessage = "The membership authority request is too large.";

        if (bytes.IsEmpty || bytes.Length > ReplicaMembershipAuthorityProtocol.MaximumRequestBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, DeserializeCallFailureMessage); }
        return NativeSerialization.Deserialize<ReplicaMembershipAuthorityCallV1>(bytes);
    }

    internal static ReplicaMembershipAuthorityReplyV1 DeserializeReply(ReadOnlySpan<byte> bytes)
    {
        const string DeserializeReplyFailureMessage = "The membership authority reply is too large.";

        if (bytes.IsEmpty || bytes.Length > ReplicaMembershipAuthorityProtocol.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, DeserializeReplyFailureMessage); }
        var reply = NativeSerialization.Deserialize<ReplicaMembershipAuthorityReplyV1>(bytes);
        ReplicaMembershipAuthorityValidation.Reply(reply);
        return reply;
    }

    private static byte[] Serialize<T>(T value, int maximumBytes)
    {
        using var stream = new ReplicaMembershipAuthorityBoundedStream(maximumBytes);
        NativeSerialization.Serialize(value, stream);
        return stream.ToArray();
    }
}
