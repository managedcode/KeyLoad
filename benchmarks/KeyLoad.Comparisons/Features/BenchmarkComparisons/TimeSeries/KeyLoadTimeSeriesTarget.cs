using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class KeyLoadTimeSeriesTarget(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> clientOptions, string? image = null)
    : ITimeSeriesPersistentTarget
{
    private const string TargetName = "KeyLoad TimeSeries";
    private const string StorageGuarantee = "RF3 process-durable storage; power-loss durability unqualified";
    private const string AcknowledgementGuarantee = "Quorum acknowledgement through the public .NET SDK";
    private const int ReadLimit = 1_000;
    private const string ConfigureCommandPurpose = "configure-resource";
    private const string SeedCommandPurpose = "seed-samples";
    private readonly KeyLoadClient client = new(http, apiKey, clientOptions);

    public TimeSeriesTargetMetadata Metadata { get; } = new(TargetName, "Persisted RF3 time-series",
        StorageGuarantee, AcknowledgementGuarantee, image, null);

    public async Task InitializeAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        var status = RequireSuccess(await client.StatusAsync(cancellationToken).ConfigureAwait(false),
            "KeyLoadStatusFailed");
        if (status.Voters != 3 || status.Durability != DurabilityProfile.QuorumProcessDurable)
        {
            throw TimeSeriesComparisonTargetErrors.Create("KeyLoadRf3Required");
        }

        var definition = new ResourceDefinition(workload.SetName, ResourceKind.TimeSeries,
            workload.Partition.TransactionDomainId);
        var configured = await client.ConfigureResourceAsync(CommandId(workload.RunId, ConfigureCommandPurpose),
            new(workload.Partition.TenantId, workload.Partition.DatabaseId, definition), cancellationToken)
            .ConfigureAwait(false);
        _ = RequireSuccess(configured, "KeyLoadResourceConfigurationFailed");
    }

    public async Task SeedAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        var samples = workload.Samples.Select(sample => new SampleData(sample.EventId,
            sample.Timestamp.ToUniversalTime(), sample.Value)).ToImmutableArray();
        var append = new AppendSamples(workload.SetName, workload.SeriesId, samples, workload.Samples[0].TagsJson);
        var command = new CommandRequest(CommandId(workload.RunId, SeedCommandPurpose), workload.Partition, [append]);
        var committed = await client.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        _ = RequireSuccess(committed, "KeyLoadSeedFailed");
    }

    public async Task<TimeSeriesReadResult> ReadAsync(TimeSeriesComparisonWorkload workload,
        TimeSeriesReadRange range, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(range);
        if (range.ExpectedErrorCode is not null || range.From > range.Until)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty,
                range.ExpectedErrorCode ?? ErrorCode.BudgetExceeded.ToString());
        }

        var request = new ReadSamplesRequest(workload.Partition, workload.SetName, workload.SeriesId,
            range.From, range.Until, ReadLimit);
        var result = await client.ReadSamplesAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty,
                result.Problem?.ErrorCode.ToString() ?? "KeyLoadReadFailed");
        }

        var samples = result.Value!.Select(record => new TimeSeriesSamplePoint(record.Sample.EventId,
            record.Sample.Timestamp.ToUniversalTime(), record.Sample.Value, record.Sequence, record.TagsJson)).ToImmutableArray();
        return new(true, samples, null);
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Guid CommandId(string runId, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(runId + ":" + purpose));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static T RequireSuccess<T>(Result<T> result, string errorCode)
    {
        if (!result.IsSuccess)
        {
            throw TimeSeriesComparisonTargetErrors.Create(errorCode);
        }

        return result.Value!;
    }
}
