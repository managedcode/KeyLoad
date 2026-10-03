using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaNativeAdmissionPolicy
{
    internal static ReplicaInspectedValue<T> Inspect<T>(ReadOnlyMemory<byte> payload, int maximumEntries, string? expectedSender = null)
    {
        try
        {
            return ReplicaNativeInspection.Inspect<T>(payload, maximumEntries, expectedSender);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }

    internal static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }
}
