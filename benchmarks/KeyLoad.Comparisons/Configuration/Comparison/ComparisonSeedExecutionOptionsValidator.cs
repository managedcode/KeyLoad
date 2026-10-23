using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Execution validation of ingestion scheduling against each original supported batch ceiling.</summary>
public sealed class ComparisonSeedExecutionOptionsValidator : IValidateOptions<ComparisonExecutionOptions>
{
    internal static readonly ComparisonSeedExecutionOptionsValidator Instance = new();

    internal static void ValidateRequired(ComparisonExecutionOptions options)
    {
        var result = Instance.Validate(Options.DefaultName, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(ComparisonExecutionOptions.SectionName,
                typeof(ComparisonExecutionOptions), result.Failures);
        }
    }

    /// <summary>Validates seed execution before any execution adapter schedules a batch.</summary>
    /// <param name="name">The execution options instance name.</param>
    /// <param name="options">The bound execution comparison execution policy.</param>
    /// <returns>The original operational validation failure or successful validation.</returns>
    public ValidateOptionsResult Validate(string? name, ComparisonExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.MongoSeedBatchSize is < 1 or > ComparisonExecutionOptions.DefaultMongoSeedBatchSize
            || options.KeyLoadDocumentSeedBatchSize is < 1 or > ComparisonExecutionOptions.DefaultKeyLoadDocumentSeedBatchSize
            || options.RedisSeedBatchSize is < 1 or > ComparisonExecutionOptions.DefaultRedisSeedBatchSize
            || options.OpenSearchBulkBatchSize is < 1 or > ComparisonExecutionOptions.DefaultOpenSearchBulkBatchSize
            ? ValidateOptionsResult.Fail(ComparisonExecutionOptions.ComparisonOperationalLimitsMustBePresentPositive)
            : ValidateOptionsResult.Success;
    }
}
