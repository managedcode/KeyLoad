using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-CQ-034: adapter limits retain their qualified ceilings and accept configured lower bounds.</summary>
internal sealed class NativeComparisonAdapterPolicyTests
{
    [Test]
    [Arguments(nameof(NativeComparisonExecutionOptions.KeyLoadGraphSeedBatchSize), 100)]
    [Arguments(nameof(NativeComparisonExecutionOptions.Neo4jSeedBatchSize), 256)]
    [Arguments(nameof(NativeComparisonExecutionOptions.Neo4jMaximumExecutionTimeSeconds), 30)]
    [Arguments(nameof(NativeComparisonExecutionOptions.TimeSeriesInitialReadCapacity), 16)]
    [Arguments(nameof(NativeComparisonExecutionOptions.KeyLoadTimeSeriesReadLimit), 1000)]
    [Arguments(nameof(NativeComparisonExecutionOptions.ReportFileBufferBytes), 65_536)]
    [Arguments(nameof(NativeComparisonExecutionOptions.TimescaleCancellationTimeoutMilliseconds), 2000)]
    [Arguments(nameof(NativeComparisonExecutionOptions.TimescaleMaximumPoolSize), 16)]
    [Arguments(nameof(NativeComparisonExecutionOptions.ReportWriterBufferCharacters), 16_384)]
    [Arguments(nameof(NativeComparisonExecutionOptions.OpenLoopEvidenceBufferBytes), 16_384)]
    [Arguments(nameof(NativeComparisonExecutionOptions.OpenLoopProofBufferBytes), 8192)]
    [Arguments(nameof(NativeComparisonExecutionOptions.OpenLoopControlFileBufferBytes), 11)]
    [Arguments(nameof(NativeComparisonExecutionOptions.VectorCancellationCheckInterval), 4096)]
    [Arguments(nameof(NativeComparisonExecutionOptions.VectorYieldBatchSize), 256)]
    [Arguments(nameof(NativeComparisonExecutionOptions.MongoSeedBatchSize), 256)]
    [Arguments(nameof(NativeComparisonExecutionOptions.KeyLoadDocumentSeedBatchSize), 100)]
    [Arguments(nameof(NativeComparisonExecutionOptions.RedisSeedBatchSize), 256)]
    [Arguments(nameof(NativeComparisonExecutionOptions.OpenSearchBulkBatchSize), 64)]
    public async Task NativeIntegerAdapterLimitsValidateInclusiveCeilingsAsync(string property, int maximum)
    {
        foreach (var accepted in new[] { maximum, 1 })
        {
            var options = UnitBenchmarkOptions.Native();
            SetInteger(options.Value, property, accepted);
            await Assert.That(NativeComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { 0, -1, maximum + 1 })
        {
            var options = UnitBenchmarkOptions.Native();
            SetInteger(options.Value, property, rejected);
            await AssertRejectedAsync(options);
        }
    }

    [Test]
    [Arguments(nameof(NativeComparisonExecutionOptions.VectorCancellationCheckInterval), 4096)]
    [Arguments(nameof(NativeComparisonExecutionOptions.VectorYieldBatchSize), 256)]
    public async Task NativeVectorCadencesRequirePowerOfTwoBelowTheirCeilingsAsync(string property, int maximum)
    {
        foreach (var accepted in new[] { 2, maximum / 2 })
        {
            var options = UnitBenchmarkOptions.Native();
            SetInteger(options.Value, property, accepted);
            await Assert.That(NativeComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { 3, maximum - 1 })
        {
            var options = UnitBenchmarkOptions.Native();
            SetInteger(options.Value, property, rejected);
            await AssertRejectedAsync(options);
        }
    }

    [Test]
    [Arguments(nameof(NativeComparisonExecutionOptions.TimescaleConnectionTimeout))]
    [Arguments(nameof(NativeComparisonExecutionOptions.TimescaleCommandTimeout))]
    public async Task NativeTimescaleDeadlinesRequirePositiveWholeSecondsWithinCeilingAsync(string property)
    {
        foreach (var accepted in new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1) })
        {
            var options = UnitBenchmarkOptions.Native();
            SetDuration(options.Value, property, accepted);
            await Assert.That(NativeComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(31), TimeSpan.FromTicks(1) })
        {
            var options = UnitBenchmarkOptions.Native();
            SetDuration(options.Value, property, rejected);
            await AssertRejectedAsync(options);
        }
    }

    [Test]
    public async Task NativeTimescaleMinimumPoolMayBeEmptyButCannotExceedTheConfiguredMaximumAsync()
    {
        foreach (var accepted in new[] { 0, 1, 16 })
        {
            var options = UnitBenchmarkOptions.Native();
            options.Value.TimescaleMinimumPoolSize = accepted;
            await Assert.That(NativeComparisonExecutionOptions.Require(options)).IsEqualTo(options);
        }

        foreach (var rejected in new[] { -1, 17 })
        {
            var options = UnitBenchmarkOptions.Native();
            options.Value.TimescaleMinimumPoolSize = rejected;
            await AssertRejectedAsync(options);
        }

        var configured = UnitBenchmarkOptions.Native();
        configured.Value.TimescaleMaximumPoolSize = 2;
        configured.Value.TimescaleMinimumPoolSize = 3;
        await AssertRejectedAsync(configured);
    }

    private static async Task AssertRejectedAsync(IOptions<NativeComparisonExecutionOptions> options)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => NativeComparisonExecutionOptions.Require(options));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(NativeComparisonExecutionOptions));
        await Assert.That(failure.OptionsName).IsEqualTo(NativeComparisonExecutionOptions.SectionName);
    }

    private static void SetInteger(NativeComparisonExecutionOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(NativeComparisonExecutionOptions.KeyLoadGraphSeedBatchSize):
                options.KeyLoadGraphSeedBatchSize = value;
                break;
            case nameof(NativeComparisonExecutionOptions.Neo4jSeedBatchSize):
                options.Neo4jSeedBatchSize = value;
                break;
            case nameof(NativeComparisonExecutionOptions.Neo4jMaximumExecutionTimeSeconds):
                options.Neo4jMaximumExecutionTimeSeconds = value;
                break;
            case nameof(NativeComparisonExecutionOptions.TimeSeriesInitialReadCapacity):
                options.TimeSeriesInitialReadCapacity = value;
                break;
            case nameof(NativeComparisonExecutionOptions.KeyLoadTimeSeriesReadLimit):
                options.KeyLoadTimeSeriesReadLimit = value;
                break;
            case nameof(NativeComparisonExecutionOptions.ReportFileBufferBytes):
                options.ReportFileBufferBytes = value;
                break;
            case nameof(NativeComparisonExecutionOptions.TimescaleCancellationTimeoutMilliseconds):
                options.TimescaleCancellationTimeoutMilliseconds = value;
                break;
            case nameof(NativeComparisonExecutionOptions.TimescaleMaximumPoolSize):
                options.TimescaleMaximumPoolSize = value;
                break;
            default:
                SetSeedOrReportInteger(options, property, value);
                break;
        }
    }

    private static void SetSeedOrReportInteger(NativeComparisonExecutionOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(NativeComparisonExecutionOptions.MongoSeedBatchSize):
                options.MongoSeedBatchSize = value;
                break;
            case nameof(NativeComparisonExecutionOptions.KeyLoadDocumentSeedBatchSize):
                options.KeyLoadDocumentSeedBatchSize = value;
                break;
            case nameof(NativeComparisonExecutionOptions.RedisSeedBatchSize):
                options.RedisSeedBatchSize = value;
                break;
            case nameof(NativeComparisonExecutionOptions.OpenSearchBulkBatchSize):
                options.OpenSearchBulkBatchSize = value;
                break;
            default:
                SetReportOrVectorInteger(options, property, value);
                break;
        }
    }

    private static void SetReportOrVectorInteger(NativeComparisonExecutionOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(NativeComparisonExecutionOptions.ReportWriterBufferCharacters):
                options.ReportWriterBufferCharacters = value;
                break;
            case nameof(NativeComparisonExecutionOptions.OpenLoopEvidenceBufferBytes):
                options.OpenLoopEvidenceBufferBytes = value;
                break;
            case nameof(NativeComparisonExecutionOptions.OpenLoopProofBufferBytes):
                options.OpenLoopProofBufferBytes = value;
                break;
            case nameof(NativeComparisonExecutionOptions.OpenLoopControlFileBufferBytes):
                options.OpenLoopControlFileBufferBytes = value;
                break;
            case nameof(NativeComparisonExecutionOptions.VectorCancellationCheckInterval):
                options.VectorCancellationCheckInterval = value;
                break;
            case nameof(NativeComparisonExecutionOptions.VectorYieldBatchSize):
                options.VectorYieldBatchSize = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }

    private static void SetDuration(NativeComparisonExecutionOptions options, string property, TimeSpan value)
    {
        switch (property)
        {
            case nameof(NativeComparisonExecutionOptions.TimescaleConnectionTimeout):
                options.TimescaleConnectionTimeout = value;
                break;
            case nameof(NativeComparisonExecutionOptions.TimescaleCommandTimeout):
                options.TimescaleCommandTimeout = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }
}
