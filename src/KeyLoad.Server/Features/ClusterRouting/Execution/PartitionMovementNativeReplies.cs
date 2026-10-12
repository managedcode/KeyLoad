using System.Security.Cryptography;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Preserves the exact existing inner native bytes and per-channel signature domains.</summary>
internal static class PartitionMovementNativeReplies
{
    internal static GrainOperationReply Safe(GrainOperationReply reply)
    {
        if (reply.Error is not null)
        { reply = reply with { SafeDetail = PartitionMovementProtocol.Unavailable }; }
        PartitionMovementTransportReplyValidation.RequireValue(reply);
        return reply;
    }

    internal static async Task<GrainOperationReply> ExecuteAsync(Func<Task<GrainOperationReply>> operation)
    {
        try { return Safe(await operation().ConfigureAwait(false)); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { return new() { Error = error.Code, SafeDetail = PartitionMovementProtocol.Unavailable }; }
    }

    internal static PartitionMovementGrainServiceReply Encode<T>(PartitionMovementNativeReceiver owner, T value,
        Func<PartitionMovementMac, byte[], string> sign)
    {
        if (NativeSerialization.Measure(value) > owner.Limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var bytes = NativeSerialization.Serialize(value);
        var key = Convert.FromBase64String(owner.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.Limits.Value.MaxBatchBytes);
            return Original(owner, bytes, sign(mac, bytes));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static PartitionMovementGrainServiceReply Original(PartitionMovementNativeReceiver owner,
        ReadOnlyMemory<byte> bytes, string signature)
    {
        if (bytes.IsEmpty || bytes.Length > owner.Limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        return new(bytes, signature, null, null, Guid.Empty, string.Empty);
    }
}
