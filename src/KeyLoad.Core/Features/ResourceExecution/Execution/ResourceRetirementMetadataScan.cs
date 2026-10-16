using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Accounts actual examined native metadata before decode without owning another read cut or deadline.</summary>
internal sealed class ResourceRetirementMetadataScan(IKeyValueView view, IOptions<DatabaseLimits> options)
{
    private readonly DatabaseLimits limits = options.Value;
    private int records;
    private long bytes;

    internal void Visit(byte[] prefix, StorageRecordVisitor visitor)
    {
        if (records >= limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
        var result = view.VisitRange(prefix, limits.MaxScanRecords - records, (key, value) =>
        {
            records += ResourceRetirementProtocol.First;
            if (key.Length > limits.MaxBatchBytes || value.Length > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return visitor(key, value);
        }, observer: Charge);
        if (result.HasMore || result.StoppedByVisitor)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
    }

    internal void Charge(long examined)
    {
        if (examined < ResourceRetirementProtocol.Empty || examined > limits.MaxQueryReadBytes - bytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
        bytes += examined;
    }

    internal static void Exact(ReadOnlySpan<byte> actual, byte[] expected)
    {
        if (!actual.SequenceEqual(expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
