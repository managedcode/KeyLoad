using System.Globalization;
using System.Numerics;

namespace KeyLoad.Comparisons;

/// <summary>Vector preparation scheduling consumed through the validated execution execution policy.</summary>
public sealed partial class ComparisonExecutionOptions
{
    private const int DefaultVectorCancellationCheckInterval = 4_096;
    private const int DefaultVectorYieldBatchSize = 256;

    /// <summary>The maximum records between cancellation checks during vector hashing and exact scans.</summary>
    public int VectorCancellationCheckInterval { get; set; } = DefaultVectorCancellationCheckInterval;
    /// <summary>The vector records streamed before yielding asynchronous ingestion control.</summary>
    public int VectorYieldBatchSize { get; set; } = DefaultVectorYieldBatchSize;

    internal int VectorCancellationCheckMask => VectorCancellationCheckInterval - 1;
    internal int VectorYieldBatchMask => VectorYieldBatchSize - 1;

    private void ValidateVectorPolicy()
    {
        if (VectorCancellationCheckInterval > DefaultVectorCancellationCheckInterval
            || !BitOperations.IsPow2(VectorCancellationCheckInterval)
            || VectorYieldBatchSize > DefaultVectorYieldBatchSize || !BitOperations.IsPow2(VectorYieldBatchSize))
        {
            throw new Microsoft.Extensions.Options.OptionsValidationException(SectionName, typeof(ComparisonExecutionOptions),
                [ComparisonOperationalLimitsMustBePresentPositive]);
        }
    }

    private void RecordVectorEvidence(IDictionary<string, string> parameters)
    {
        parameters[nameof(VectorCancellationCheckInterval)] = VectorCancellationCheckInterval.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(VectorYieldBatchSize)] = VectorYieldBatchSize.ToString(CultureInfo.InvariantCulture);
    }
}
