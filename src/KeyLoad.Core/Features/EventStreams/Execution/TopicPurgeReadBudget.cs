using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal sealed class TopicPurgeReadBudget
{
    private const string Exhausted = "The topic purge scan budget is exhausted.";
    private readonly DatabaseLimits limits;

    internal TopicPurgeReadBudget(IOptions<DatabaseLimits> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
    }

    private long records;
    private long bytes;

    internal void ObserveBytes(long count)
    {
        // Native observers run once before each owned decode, including range lookahead.
        if (++records > limits.MaxScanRecords || count > limits.MaxBatchBytes - bytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Exhausted); }
        bytes += count;
    }
}
