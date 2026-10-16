using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Reuses the native peer MAC under closed original-observation domains.</summary>
internal static class CommandObservationNativeMac
{
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(CommandObservationNativeProtocol.RequestPurpose);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(CommandObservationNativeProtocol.ReplyPurpose);
    private static readonly string SignatureWidth = new('0', ReplicaTransportProtocol.HashHexCharacters);

    internal static PartitionMovementMac Create(NodeOptions options, PartitionHost partition)
    {
        var key = Convert.FromBase64String(options.PeerSecret);
        try
        {
            return new(key, partition.Database.Limits.MaxBatchBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static string Sign(PartitionMovementMac mac, CommandObservationNativeRequest request)
        => Digest(mac, request with
        {
            Signature = string.Empty
        }, RequestDomain);

    internal static bool Verify(PartitionMovementMac mac, CommandObservationNativeRequest request)
        => Verify(mac, request with
        {
            Signature = string.Empty
        }, request.Signature, RequestDomain);

    internal static string Sign(PartitionMovementMac mac, CommandObservationNativeReply reply)
        => Digest(mac, reply with
        {
            Signature = string.Empty
        }, ReplyDomain);

    internal static bool Verify(PartitionMovementMac mac, CommandObservationNativeReply reply)
        => Verify(mac, reply with
        {
            Signature = string.Empty
        }, reply.Signature, ReplyDomain);

    internal static void RequireSize<T>(T value, PartitionHost partition)
    {
        var maximum = checked(partition.Database.Limits.MaxBatchBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes);
        if (NativeSerialization.Measure(value) > maximum)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CommandObservationNativeProtocol.Invalid);
        }
    }

    internal static CommandObservationNativeReply SignReply(PartitionMovementMac mac,
        CommandObservationNativeRequest request, GrainOperationReply result, ReadExecutionBudget work, PartitionHost partition)
    {
        var reply = new CommandObservationNativeReply(request.Nonce, request.Signature, result, SignatureWidth);
        RequireSize(reply, partition);
        if (NativeSerialization.Measure(reply) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        var signed = reply with
        {
            Signature = Sign(mac, reply)
        };
        RequireSize(signed, partition);
        if (NativeSerialization.Measure(signed) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        work.Check();
        return signed;
    }

    private static string Digest<T>(PartitionMovementMac mac, T value, byte[] domain)
    {
        var bytes = NativeSerialization.Serialize(value);
        try
        {
            return Convert.ToHexStringLower(mac.DigestNative(bytes, domain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool Verify<T>(PartitionMovementMac mac, T value, string signature, byte[] domain)
    {
        var bytes = NativeSerialization.Serialize(value);
        try
        {
            return mac.VerifyNative(bytes, signature, domain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
