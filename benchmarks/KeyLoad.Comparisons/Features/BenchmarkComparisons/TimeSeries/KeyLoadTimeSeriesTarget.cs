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
    private const string PersistedRF3TimeSeriesToken = "Persisted RF3 time-series";

    private const string TargetName = "KeyLoad TimeSeries";
    private const string StorageGuarantee = "RF3 process-durable storage; power-loss durability unqualified";
    private const string AcknowledgementGuarantee = "Quorum acknowledgement through the public .NET SDK";
    private const int ReadLimit = 1_000;
    private const string ConfigureCommandPurpose = "configure-resource";
    private const string SeedCommandPurpose = "seed-samples";
    private readonly KeyLoadClient client = new(http, apiKey, clientOptions);

    public TimeSeriesTargetMetadata Metadata { get; } = new(TargetName, PersistedRF3TimeSeriesToken,
        StorageGuarantee, AcknowledgementGuarantee, image, null);

    public async Task InitializeAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        const string KeyLoadResourceConfigurationFailedToken = "KeyLoadResourceConfigurationFailed";

        const string KeyLoadStatusFailedToken = "KeyLoadStatusFailed";
        const int ThreeNodeTopology = 3;
        const string KeyLoadRf3RequiredDetail = "KeyLoadRf3Required";

        ArgumentNullException.ThrowIfNull(workload);
        var status = RequireSuccess(await client.StatusAsync(cancellationToken).ConfigureAwait(false),
            KeyLoadStatusFailedToken);
        if (status.Voters != ThreeNodeTopology || status.Durability != DurabilityProfile.QuorumProcessDurable)
        {
            throw TimeSeriesComparisonTargetErrors.Create(KeyLoadRf3RequiredDetail);
        }

        var definition = new ResourceDefinition(workload.SetName, ResourceKind.TimeSeries,
            workload.Partition.TransactionDomainId);
        var configured = await client.ConfigureResourceAsync(CommandId(workload.RunId, ConfigureCommandPurpose),
            new(workload.Partition.TenantId, workload.Partition.DatabaseId, definition), cancellationToken)
            .ConfigureAwait(false);
        _ = RequireSuccess(configured, KeyLoadResourceConfigurationFailedToken);
    }

    public async Task SeedAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;
        const string KeyLoadSeedFailedToken = "KeyLoadSeedFailed";

        ArgumentNullException.ThrowIfNull(workload);
        var samples = workload.Samples.Select(sample => new SampleData(sample.EventId,
            sample.Timestamp.ToUniversalTime(), sample.Value)).ToImmutableArray();
        var append = new AppendSamples(workload.SetName, workload.SeriesId, samples, workload.Samples[FirstElementIndex].TagsJson);
        var command = new CommandRequest(CommandId(workload.RunId, SeedCommandPurpose), workload.Partition, [append]);
        var committed = await client.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        _ = RequireSuccess(committed, KeyLoadSeedFailedToken);
    }

    public async Task<TimeSeriesReadResult> ReadAsync(TimeSeriesComparisonWorkload workload,
        TimeSeriesReadRange range, CancellationToken cancellationToken)
    {
        const string KeyLoadReadFailedToken = "KeyLoadReadFailed";

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
                result.Problem?.ErrorCode.ToString() ?? KeyLoadReadFailedToken);
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
        const string FieldSeparator = ":";
        const int FirstElementIndex = 0;
        const int GuidDigestBytes = 16;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(runId + FieldSeparator + purpose));
        return new Guid(bytes.AsSpan(FirstElementIndex, GuidDigestBytes));
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
