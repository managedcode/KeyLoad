using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnProjectionNativeEncoding
{
    internal static (AnnProjectionNativeCall Call, byte[] Bytes) EncodeCall(AnnProjectionNativeCall original,
        ReadExecutionBudget work, long maximum)
    {
        var admitted = Admit(original, work, maximum);
        var call = original with
        {
            Observation = Remaining(original.Observation, work)
        };
        return (call, EncodeAdmitted(call, admitted));
    }

    internal static (AnnProjectionNativeCall Call, byte[] Bytes) EncodeChild(AnnProjectionNativeCall original,
        ReadExecutionBudget work, long maximum)
    {
        var admitted = Admit(original.Observation, work, maximum);
        var call = original with
        {
            Observation = Remaining(original.Observation, work)
        };
        return (call, EncodeAdmitted(call.Observation, admitted));
    }

    private static AnnProjectionObservationRequest Remaining(AnnProjectionObservationRequest original,
        ReadExecutionBudget work)
    {
        var bytes = Math.Min(original.MaximumReadBytes, work.RemainingReadGrantBytes);
        var records = Math.Min(original.MaximumRecords, work.RemainingReadGrantRecords);
        if (bytes <= AnnProjectionObservationProtocol.NoReadWork || records <= AnnProjectionObservationProtocol.NoReadWork)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProjectionObservationProtocol.Invalid);
        }
        return original with
        {
            MaximumReadBytes = bytes,
            MaximumRecords = records
        };
    }

    private static long Admit<T>(T value, ReadExecutionBudget work, long maximum)
    {
        work.Check();
        var measured = NativeSerialization.Measure(value);
        if (measured > maximum)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProjectionObservationProtocol.Invalid);
        }
        work.ChargeBytes(measured);
        return measured;
    }

    private static byte[] EncodeAdmitted<T>(T value, long admission)
    {
        var measured = NativeSerialization.Measure(value);
        if (measured > admission)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProjectionObservationProtocol.Invalid);
        }
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length == measured)
        {
            return bytes;
        }
        CryptographicOperations.ZeroMemory(bytes);
        throw Errors.Fail(ErrorCode.Corruption, AnnProjectionObservationProtocol.Invalid);
    }

    internal static byte[] Encode<T>(T value, ReadExecutionBudget work, long maximum)
    {
        work.Check();
        var measured = NativeSerialization.Measure(value);
        if (measured > maximum)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProjectionObservationProtocol.Invalid);
        }
        work.ChargeBytes(measured);
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length == measured)
        {
            return bytes;
        }
        CryptographicOperations.ZeroMemory(bytes);
        throw Errors.Fail(ErrorCode.Corruption, AnnProjectionObservationProtocol.Invalid);
    }
}
