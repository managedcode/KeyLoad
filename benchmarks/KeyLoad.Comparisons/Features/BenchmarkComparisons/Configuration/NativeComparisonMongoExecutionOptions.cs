using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Mongo connection pool policy owned by the native execution group.</summary>
public sealed partial class NativeComparisonExecutionOptions
{
    private const int DefaultMongoPoolSessionMargin = 4;
    private const int DefaultMongoPoolMinimumSize = 16;

    /// <summary>The reserved connections added to the configured workload concurrency.</summary>
    public int MongoPoolSessionMargin { get; set; } = DefaultMongoPoolSessionMargin;
    /// <summary>The floor applied to the maximum native Mongo connection pool size.</summary>
    public int MongoPoolMinimumSize { get; set; } = DefaultMongoPoolMinimumSize;

    private void ValidateMongoPolicy()
    {
        if (MongoPoolSessionMargin is <= MinimumPositiveLimit or > DefaultMongoPoolSessionMargin
            || MongoPoolMinimumSize is <= MinimumPositiveLimit or > DefaultMongoPoolMinimumSize)
        {
            throw new OptionsValidationException(SectionName, typeof(NativeComparisonExecutionOptions),
                [NativeComparisonOperationalLimitsMustBePresentPositive]);
        }
    }

    private void RecordMongoEvidence(IDictionary<string, string> parameters)
    {
        parameters[nameof(MongoPoolSessionMargin)] = MongoPoolSessionMargin.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(MongoPoolMinimumSize)] = MongoPoolMinimumSize.ToString(CultureInfo.InvariantCulture);
    }
}
