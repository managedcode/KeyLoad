using Microsoft.Extensions.Options;
namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityCodec
{
    internal static byte[] SerializeCall(ReplicaMembershipAuthorityCallV1 call, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        ReplicaMembershipAuthorityValidation.Call(call: call, membershipOptions: membershipOptions);
        return Serialize(call, membershipOptions.Value.MaximumRequestBytes);
    }

    internal static byte[] SerializeReply(ReplicaMembershipAuthorityReplyV1 reply, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        ReplicaMembershipAuthorityValidation.Reply(reply: reply, membershipOptions: membershipOptions);
        return Serialize(reply, membershipOptions.Value.MaximumReplyBytes);
    }

    internal static ReplicaMembershipAuthorityCallV1 DeserializeCall(ReadOnlySpan<byte> bytes, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        const string DeserializeCallFailureMessage = "The membership authority request is too large.";

        if (bytes.IsEmpty || bytes.Length > membershipOptions.Value.MaximumRequestBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, DeserializeCallFailureMessage); }
        return NativeSerialization.Deserialize<ReplicaMembershipAuthorityCallV1>(bytes);
    }

    internal static ReplicaMembershipAuthorityReplyV1 DeserializeReply(ReadOnlySpan<byte> bytes, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        const string DeserializeReplyFailureMessage = "The membership authority reply is too large.";

        if (bytes.IsEmpty || bytes.Length > membershipOptions.Value.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, DeserializeReplyFailureMessage); }
        var reply = NativeSerialization.Deserialize<ReplicaMembershipAuthorityReplyV1>(bytes);
        ReplicaMembershipAuthorityValidation.Reply(reply: reply, membershipOptions: membershipOptions);
        return reply;
    }

    private static byte[] Serialize<T>(T value, int maximumBytes)
    {
        using var stream = new ReplicaMembershipAuthorityBoundedStream(maximumBytes);
        NativeSerialization.Serialize(value, stream);
        return stream.ToArray();
    }
}
