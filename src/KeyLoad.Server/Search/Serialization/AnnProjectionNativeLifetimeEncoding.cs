using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnProjectionNativeLifetimeEncoding
{
    internal static (AnnProjectionNativeCallV2 Call, byte[] Bytes) EncodeCall(AnnProjectionNativeCallV2 original,
        ReadExecutionBudget work, long maximum)
    {
        var admitted = Admit(original, work, maximum);
        var call = Remaining(original, work);
        return (call, EncodeAdmitted(call, admitted));
    }

    internal static (AnnProjectionNativeCallV2 Call, byte[] Bytes) EncodeChild(AnnProjectionNativeCallV2 original,
        ReadExecutionBudget work, long maximum)
    {
        var admitted = Admit(Child(original), work, maximum);
        var call = Remaining(original, work);
        return (call, EncodeAdmitted(Child(call), admitted));
    }

    private static AnnProjectionObservationRequestV2 Child(AnnProjectionNativeCallV2 call)
        => new(call.Original.Observation, call.Definition, call.Coverage, call.Principal, call.Definitions);

    private static AnnProjectionNativeCallV2 Remaining(AnnProjectionNativeCallV2 original, ReadExecutionBudget work)
    {
        var observation = original.Original.Observation;
        var bytes = Math.Min(observation.MaximumReadBytes, work.RemainingReadGrantBytes);
        var records = Math.Min(observation.MaximumRecords, work.RemainingReadGrantRecords);
        if (bytes <= AnnProjectionObservationProtocol.NoReadWork || records <= AnnProjectionObservationProtocol.NoReadWork)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProjectionObservationProtocol.Invalid);
        }
        return original with
        {
            Original = original.Original with
            {
                Observation = observation with
                {
                    MaximumReadBytes = bytes,
                    MaximumRecords = records
                }
            }
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
}
