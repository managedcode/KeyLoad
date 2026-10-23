using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes QueueRetrySelection operations against the caller's database context.</summary>
public static class QueueRetrySelection
{
    internal static int QueueRetryCap(this DatabaseEngine engine, QueuePolicy policy, int attempts)
    {
        if (attempts < QueueRetryProtocol.Positive || policy.RetryBaseMilliseconds < QueueRetryProtocol.Positive || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds || policy.RetryExponentialFactor < QueueRetryProtocol.Positive)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision);
        }

        var cap = (long)policy.RetryBaseMilliseconds;
        var exponent = Math.Min(attempts - QueueRetryProtocol.Positive, engine.MessagingExecution.MaximumRetryExponent);
        for (var step = QueueRetryProtocol.Initial; step < exponent; step++)
        {
            if (cap > policy.RetryMaxMilliseconds / policy.RetryExponentialFactor)
            {
                return policy.RetryMaxMilliseconds;
            }

            cap *= policy.RetryExponentialFactor;
        }

        return (int)Math.Min(cap, policy.RetryMaxMilliseconds);
    }

    internal static int ChooseQueueRetryDelay(this DatabaseEngine engine, Command<DatabaseCommand> original, QueueLaneRef lane, QueuePolicy policy, MessageMetadata metadata)
    {
        var cap = engine.QueueRetryCap(policy, metadata.Attempts);
        if (policy.RetryJitter == QueueRetryJitter.None)
        {
            return cap;
        }

        if (policy.RetryJitter != QueueRetryJitter.Full)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision);
        }

        var seed = OrleansSerialization.Serialize(new QueueRetrySeed(original.CommandId, lane, metadata.Id, metadata.DeliveryGeneration, metadata.Attempts, metadata.StateVersion));
        engine.RequireBudget(seed.Length);
        Span<byte> digest = stackalloc byte[QueueRetryProtocol.DigestBytes];
        _ = HMACSHA256.HashData(engine.Store.Identity.SigningKey.Span, seed, digest);
        var draw = BinaryPrimitives.ReadUInt64BigEndian(digest);
        return checked(QueueRetryProtocol.Positive + (int)(draw % (ulong)cap));
    }
}
