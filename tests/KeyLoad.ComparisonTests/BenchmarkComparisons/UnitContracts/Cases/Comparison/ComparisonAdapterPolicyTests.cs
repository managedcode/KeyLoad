using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-CQ-034: adapter limits retain their qualified ceilings and accept configured lower bounds.</summary>
internal sealed class ComparisonAdapterPolicyTests
{
    [Test]
    [Arguments(nameof(ComparisonExecutionOptions.KeyLoadGraphSeedBatchSize), 100)]
    [Arguments(nameof(ComparisonExecutionOptions.Neo4jSeedBatchSize), 256)]
    [Arguments(nameof(ComparisonExecutionOptions.Neo4jMaximumExecutionTimeSeconds), 30)]
    [Arguments(nameof(ComparisonExecutionOptions.TimeSeriesInitialReadCapacity), 16)]
    [Arguments(nameof(ComparisonExecutionOptions.KeyLoadTimeSeriesReadLimit), 1000)]
    [Arguments(nameof(ComparisonExecutionOptions.ReportFileBufferBytes), 65_536)]
    [Arguments(nameof(ComparisonExecutionOptions.OpenSearchHealthWaitTimeoutSeconds), 60)]
    [Arguments(nameof(ComparisonExecutionOptions.TimescaleCancellationTimeoutMilliseconds), 2000)]
    [Arguments(nameof(ComparisonExecutionOptions.TimescaleMaximumPoolSize), 16)]
    [Arguments(nameof(ComparisonExecutionOptions.VectorCancellationCheckInterval), 4096)]
    [Arguments(nameof(ComparisonExecutionOptions.VectorYieldBatchSize), 256)]
    [Arguments(nameof(ComparisonExecutionOptions.MongoSeedBatchSize), 256)]
    [Arguments(nameof(ComparisonExecutionOptions.KeyLoadDocumentSeedBatchSize), 100)]
    [Arguments(nameof(ComparisonExecutionOptions.RedisSeedBatchSize), 256)]
    [Arguments(nameof(ComparisonExecutionOptions.OpenSearchBulkBatchSize), 64)]
    public async Task IntegerAdapterLimitsValidateInclusiveCeilingsAsync(string property, int maximum)
    {
        foreach (var accepted in new[] { maximum, 1 })
        {
            var options = UnitBenchmarkOptions.Execution();
            SetInteger(options.Value, property, accepted);
            await Assert.That(ComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { 0, -1, maximum + 1 })
        {
            var options = UnitBenchmarkOptions.Execution();
            SetInteger(options.Value, property, rejected);
            await AssertRejectedAsync(options);
        }
    }

    [Test]
    [Arguments(nameof(ComparisonExecutionOptions.VectorCancellationCheckInterval), 4096)]
    [Arguments(nameof(ComparisonExecutionOptions.VectorYieldBatchSize), 256)]
    public async Task VectorCadencesRequirePowerOfTwoBelowTheirCeilingsAsync(string property, int maximum)
    {
        foreach (var accepted in new[] { 2, maximum / 2 })
        {
            var options = UnitBenchmarkOptions.Execution();
            SetInteger(options.Value, property, accepted);
            await Assert.That(ComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { 3, maximum - 1 })
        {
            var options = UnitBenchmarkOptions.Execution();
            SetInteger(options.Value, property, rejected);
            await AssertRejectedAsync(options);
        }
    }

    [Test]
    [Arguments(nameof(ComparisonExecutionOptions.TimescaleConnectionTimeout))]
    [Arguments(nameof(ComparisonExecutionOptions.TimescaleCommandTimeout))]
    public async Task TimescaleDeadlinesRequirePositiveWholeSecondsWithinCeilingAsync(string property)
    {
        foreach (var accepted in new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1) })
        {
            var options = UnitBenchmarkOptions.Execution();
            SetDuration(options.Value, property, accepted);
            await Assert.That(ComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(31), TimeSpan.FromTicks(1) })
        {
            var options = UnitBenchmarkOptions.Execution();
            SetDuration(options.Value, property, rejected);
            await AssertRejectedAsync(options);
        }
    }

    [Test]
    public async Task TimescaleMinimumPoolMayBeEmptyButCannotExceedTheConfiguredMaximumAsync()
    {
        foreach (var accepted in new[] { 0, 1, 16 })
        {
            var options = UnitBenchmarkOptions.Execution();
            options.Value.TimescaleMinimumPoolSize = accepted;
            await Assert.That(ComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { -1, 17 })
        {
            var options = UnitBenchmarkOptions.Execution();
            options.Value.TimescaleMinimumPoolSize = rejected;
            await AssertRejectedAsync(options);
        }

        var configured = UnitBenchmarkOptions.Execution();
        configured.Value.TimescaleMaximumPoolSize = 2;
        configured.Value.TimescaleMinimumPoolSize = 3;
        await AssertRejectedAsync(configured);
    }

    private static async Task AssertRejectedAsync(IOptions<ComparisonExecutionOptions> options)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => ComparisonExecutionOptions.Require(options));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ComparisonExecutionOptions));
        await Assert.That(failure.OptionsName).IsEqualTo(ComparisonExecutionOptions.SectionName);
    }

    private static void SetInteger(ComparisonExecutionOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(ComparisonExecutionOptions.KeyLoadGraphSeedBatchSize):
                options.KeyLoadGraphSeedBatchSize = value;
                break;
            case nameof(ComparisonExecutionOptions.Neo4jSeedBatchSize):
                options.Neo4jSeedBatchSize = value;
                break;
            case nameof(ComparisonExecutionOptions.Neo4jMaximumExecutionTimeSeconds):
                options.Neo4jMaximumExecutionTimeSeconds = value;
                break;
            case nameof(ComparisonExecutionOptions.TimeSeriesInitialReadCapacity):
                options.TimeSeriesInitialReadCapacity = value;
                break;
            case nameof(ComparisonExecutionOptions.KeyLoadTimeSeriesReadLimit):
                options.KeyLoadTimeSeriesReadLimit = value;
                break;
            case nameof(ComparisonExecutionOptions.ReportFileBufferBytes):
                options.ReportFileBufferBytes = value;
                break;
            case nameof(ComparisonExecutionOptions.TimescaleCancellationTimeoutMilliseconds):
                options.TimescaleCancellationTimeoutMilliseconds = value;
                break;
            case nameof(ComparisonExecutionOptions.TimescaleMaximumPoolSize):
                options.TimescaleMaximumPoolSize = value;
                break;
            case nameof(ComparisonExecutionOptions.OpenSearchHealthWaitTimeoutSeconds):
                options.OpenSearchHealthWaitTimeoutSeconds = value;
                break;
            default:
                SetSeedOrReportInteger(options, property, value);
                break;
        }
    }

    private static void SetSeedOrReportInteger(ComparisonExecutionOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(ComparisonExecutionOptions.MongoSeedBatchSize):
                options.MongoSeedBatchSize = value;
                break;
            case nameof(ComparisonExecutionOptions.KeyLoadDocumentSeedBatchSize):
                options.KeyLoadDocumentSeedBatchSize = value;
                break;
            case nameof(ComparisonExecutionOptions.RedisSeedBatchSize):
                options.RedisSeedBatchSize = value;
                break;
            case nameof(ComparisonExecutionOptions.OpenSearchBulkBatchSize):
                options.OpenSearchBulkBatchSize = value;
                break;
            default:
                SetReportOrVectorInteger(options, property, value);
                break;
        }
    }

    private static void SetReportOrVectorInteger(ComparisonExecutionOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(ComparisonExecutionOptions.VectorCancellationCheckInterval):
                options.VectorCancellationCheckInterval = value;
                break;
            case nameof(ComparisonExecutionOptions.VectorYieldBatchSize):
                options.VectorYieldBatchSize = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }

    private static void SetDuration(ComparisonExecutionOptions options, string property, TimeSpan value)
    {
        switch (property)
        {
            case nameof(ComparisonExecutionOptions.TimescaleConnectionTimeout):
                options.TimescaleConnectionTimeout = value;
                break;
            case nameof(ComparisonExecutionOptions.TimescaleCommandTimeout):
                options.TimescaleCommandTimeout = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }
}
