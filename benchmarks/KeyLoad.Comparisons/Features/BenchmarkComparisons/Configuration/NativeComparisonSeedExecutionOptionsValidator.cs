using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Native validation of ingestion scheduling against each original supported batch ceiling.</summary>
public sealed class NativeComparisonSeedExecutionOptionsValidator : IValidateOptions<NativeComparisonExecutionOptions>
{
    private const int MinimumSeedBatchSize = 1;
    internal static readonly NativeComparisonSeedExecutionOptionsValidator Instance = new();

    internal static void ValidateRequired(NativeComparisonExecutionOptions options)
    {
        var result = Instance.Validate(Options.DefaultName, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(NativeComparisonExecutionOptions.SectionName,
                typeof(NativeComparisonExecutionOptions), result.Failures);
        }
    }

    /// <summary>Validates seed execution before any native adapter schedules a batch.</summary>
    /// <param name="name">The native options instance name.</param>
    /// <param name="options">The bound native comparison execution policy.</param>
    /// <returns>The original operational validation failure or successful validation.</returns>
    public ValidateOptionsResult Validate(string? name, NativeComparisonExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.MongoSeedBatchSize is < MinimumSeedBatchSize or > NativeComparisonExecutionOptions.DefaultMongoSeedBatchSize
            || options.KeyLoadDocumentSeedBatchSize is < MinimumSeedBatchSize or > NativeComparisonExecutionOptions.DefaultKeyLoadDocumentSeedBatchSize
            || options.RedisSeedBatchSize is < MinimumSeedBatchSize or > NativeComparisonExecutionOptions.DefaultRedisSeedBatchSize
            || options.OpenSearchBulkBatchSize is < MinimumSeedBatchSize or > NativeComparisonExecutionOptions.DefaultOpenSearchBulkBatchSize
            ? ValidateOptionsResult.Fail(NativeComparisonExecutionOptions.NativeComparisonOperationalLimitsMustBePresentPositive)
            : ValidateOptionsResult.Success;
    }
}
