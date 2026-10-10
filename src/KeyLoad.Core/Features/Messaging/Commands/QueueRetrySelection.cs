using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private int QueueRetryCap(QueuePolicy policy, int attempts)
    {
        if (attempts < QueueRetryProtocol.Positive || policy.RetryBaseMilliseconds < QueueRetryProtocol.Positive
            || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds || policy.RetryExponentialFactor < QueueRetryProtocol.Positive)
        { throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision); }
        var cap = (long)policy.RetryBaseMilliseconds;
        var exponent = Math.Min(attempts - QueueRetryProtocol.Positive, messagingExecution.MaximumRetryExponent);
        for (var step = QueueRetryProtocol.Initial; step < exponent; step++)
        {
            if (cap > policy.RetryMaxMilliseconds / policy.RetryExponentialFactor)
            { return policy.RetryMaxMilliseconds; }
            cap *= policy.RetryExponentialFactor;
        }
        return (int)Math.Min(cap, policy.RetryMaxMilliseconds);
    }

    private int ChooseQueueRetryDelay(ReplicatedOperation original, QueueLaneRef lane, QueuePolicy policy,
        MessageMetadata metadata)
    {
        var cap = QueueRetryCap(policy, metadata.Attempts);
        if (policy.RetryJitter == QueueRetryJitter.None)
        { return cap; }
        if (policy.RetryJitter != QueueRetryJitter.Full)
        { throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision); }
        var seed = NativeSerialization.Serialize(new QueueRetrySeed(original.Id, lane, metadata.Id,
            metadata.DeliveryGeneration, metadata.Attempts, metadata.StateVersion));
        RequireNativeBudget(seed.Length);
        Span<byte> digest = stackalloc byte[QueueRetryProtocol.DigestBytes];
        _ = HMACSHA256.HashData(Store.Identity.SigningKey.Span, seed, digest);
        var sample = BinaryPrimitives.ReadUInt64BigEndian(digest);
        return checked(QueueRetryProtocol.Positive + (int)(sample % (ulong)cap));
    }
}
