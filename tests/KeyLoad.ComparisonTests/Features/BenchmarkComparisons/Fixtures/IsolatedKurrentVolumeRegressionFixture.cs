using System.Globalization;
using System.Runtime.ExceptionServices;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using Microsoft.Extensions.Options;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKurrentVolumeRegressionFixture(Uri endpoint, ComparisonOptions options,
    IOptions<NativeComparisonHarnessOptions> harnessOptions)
{
    private const int CanonicalCount = 55_378;
    private readonly Guid scope = Guid.NewGuid();
    private readonly List<KurrentDBClient> clients = [];
    private readonly KurrentStreamOwnership ownership = new(NativeExecutionPolicyFixture.Workload(options));
    private readonly KurrentEventData[] expectedEvents = new KurrentEventData[CanonicalCount];
    private KurrentDBClient? writer;
    private string[] streams = [];
    private string foreign = string.Empty;
    private KurrentEventData? foreignEvent;
    private IsolatedKurrentOwnershipRegressionNative.Snapshot? foreignBefore;
    private bool foreignAcknowledged;
    private bool cleanupAttempted;

    internal async Task SeedAsync(CancellationToken token)
    {
        var independentlyDerived = checked(options.Documents + options.Repetitions * checked(options.Warmup + options.Operations)
            + 2);
        await Assert.That(IsolatedKurrentVolumeRegressionNative.ReadOwnershipProbeCount()).IsEqualTo(2);
        await Assert.That(independentlyDerived).IsEqualTo(CanonicalCount);
        await Assert.That(ownership.Capacity).IsEqualTo(independentlyDerived);
        writer = CreateClient();
        var foreignWriter = CreateClient();
        foreign = Prefix + "foreign";
        var foreignData = CreateForeignEvent();
        foreignEvent = foreignData;
        var foreignResult = await foreignWriter.AppendToStreamAsync(foreign, StreamState.NoStream, [foreignData], cancellationToken: token);
        if (foreignResult is not SuccessResult)
        {
            throw new ComparisonFailureException(KurrentConstants.AppendAcknowledgementMissing);
        }
        foreignAcknowledged = true;
        foreignBefore = await IsolatedKurrentOwnershipRegressionNative.ReadOriginalAsync(CreateClient(), foreign, token);
        streams = Enumerable.Range(0, ownership.Capacity)
            .Select(index => Prefix + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        await IsolatedKurrentVolumeRegressionWorkers.RunAsync(streams.Length, SeedOneAsync, harnessOptions, token);
        await Assert.That(ownership.SnapshotAcknowledged().Length).IsEqualTo(ownership.Capacity);
    }

    internal async Task CleanupAndVerifyAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var acknowledged = ownership.SnapshotAcknowledged();
        await Assert.That(acknowledged.Length).IsEqualTo(ownership.Capacity);
        await Assert.That(acknowledged.ToHashSet(StringComparer.Ordinal).SetEquals(streams)).IsTrue();
        var preDeleteReader = CreateClient();
        await IsolatedKurrentVolumeRegressionWorkers.RunAsync(streams.Length,
            (index, workerToken) => RequireOriginalAsync(preDeleteReader, index, workerToken), harnessOptions, token);
        var originalClients = clients.ToArray();
        clients.Clear();
        cleanupAttempted = true;
        var cleanupWriter = writer;
        writer = null;
        var diagnostic = await KurrentOwnedStreamCleanup.RunAsync(cleanupWriter, acknowledged, originalClients, [], NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, CancellationToken.None);
        await IsolatedKurrentVolumeRegressionNative.RequireCompleteAsync(diagnostic, ownership.Capacity);
        writer = null;
        var reader = CreateClient();
        await IsolatedKurrentVolumeRegressionWorkers.RunAsync(acknowledged.Length,
            (index, workerToken) => IsolatedKurrentVolumeRegressionNative.RequireDeletedAsync(reader,
                acknowledged[index], workerToken), harnessOptions, token);
        await RequireForeignUnchangedAsync(reader, token);
    }

    internal async Task CleanupRemainingAsync(ExceptionDispatchInfo? primary)
    {
        var failures = new IsolatedKurrentVolumeRegressionFailureCollector();
        if (!cleanupAttempted)
        {
            cleanupAttempted = true;
            await failures.AttemptCleanupAsync(async () =>
            {
                var acknowledged = ownership.SnapshotAcknowledged();
                var cleanupWriter = writer;
                writer = null;
                var diagnostic = await TransferCleanupAsync(cleanupWriter, acknowledged);
                await IsolatedKurrentVolumeRegressionNative.RequireCompleteAsync(diagnostic, acknowledged.Length);
            });
        }

        if (foreignAcknowledged && !foreignCleanupAttempted)
        {
            foreignCleanupAttempted = true;
            await failures.AttemptCleanupAsync(async () =>
            {
                var foreignWriter = CreateClient();
                var foreignDiagnostic = await TransferCleanupAsync(foreignWriter, [foreign]);
                await IsolatedKurrentVolumeRegressionNative.RequireCompleteAsync(foreignDiagnostic, 1);
            });
        }

        if (clients.Count > 0)
        {
            await failures.AttemptCleanupAsync(async () =>
            {
                var disposalDiagnostic = await TransferCleanupAsync(null, []);
                await IsolatedKurrentVolumeRegressionNative.RequireCompleteAsync(disposalDiagnostic, 0);
            });
        }
        failures.ThrowAfterCleanup(primary);
    }

    private bool foreignCleanupAttempted;

    private async Task<KurrentCleanupDiagnostic> TransferCleanupAsync(KurrentDBClient? cleanupWriter, string[] streamsToDelete)
    {
        var ownedClients = clients.ToArray();
        clients.Clear();
        return await KurrentOwnedStreamCleanup.RunAsync(cleanupWriter, streamsToDelete, ownedClients, [], NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, CancellationToken.None);
    }

    private async Task SeedOneAsync(int index, CancellationToken token)
    {
        var stream = streams[index];
        var data = CreateEvent();
        expectedEvents[index] = data;
        ownership.Reserve(stream, data.EventId.ToGuid());
        var result = await RequireWriter().AppendToStreamAsync(stream, StreamState.NoStream, [data], cancellationToken: token);
        if (result is not SuccessResult)
        {
            throw new ComparisonFailureException(KurrentConstants.AppendAcknowledgementMissing);
        }
        ownership.Acknowledge(stream, data.EventId.ToGuid());
    }

    private async Task RequireOriginalAsync(KurrentDBClient reader, int index, CancellationToken token)
    {
        var expected = expectedEvents[index];
        var actual = await IsolatedKurrentOwnershipRegressionNative.ReadOriginalAsync(reader, streams[index], token);
        await Assert.That(actual.EventId).IsEqualTo(expected.EventId.ToGuid());
        await Assert.That(actual.Revision).IsEqualTo(KurrentConstants.NativeFirstRevision);
        await Assert.That(actual.Data.AsSpan().SequenceEqual(expected.Data.ToArray())).IsTrue();
        await Assert.That(actual.Type).IsEqualTo(KurrentConstants.EventType);
        await Assert.That(actual.ContentType).IsEqualTo(KurrentConstants.EventJson);
        await Assert.That(actual.Metadata.AsSpan().SequenceEqual(expected.Metadata.ToArray())).IsTrue();
    }

    private async Task RequireForeignUnchangedAsync(KurrentDBClient reader, CancellationToken token)
    {
        if (!foreignAcknowledged || foreignEvent is null || foreignBefore is null)
        {
            throw new ComparisonFailureException(KurrentConstants.AppendAcknowledgementMissing);
        }
        var after = await IsolatedKurrentOwnershipRegressionNative.ReadOriginalAsync(reader, foreign, token);
        await IsolatedKurrentOwnershipRegressionNative.RequireUnchangedAsync(foreignBefore, after);
        await Assert.That(after.EventId).IsEqualTo(foreignEvent.EventId.ToGuid());
        await Assert.That(after.Data.AsSpan().SequenceEqual(foreignEvent.Data.ToArray())).IsTrue();
    }

    private KurrentDBClient CreateClient()
    {
        var client = IsolatedKurrentCleanupRegressionNative.CreateClient(endpoint);
        clients.Add(client);
        return client;
    }

    private KurrentDBClient RequireWriter()
        => writer ?? throw new InvalidOperationException(KurrentConstants.NotInitialized);

    private string Prefix => KurrentConstants.StreamPrefix + scope.ToString(KurrentConstants.GuidFormat)
        + KurrentConstants.StreamSeparator;

    private static KurrentEventData CreateEvent() => IsolatedKurrentVolumeRegressionNative.CreateEvent();
    private static KurrentEventData CreateForeignEvent() => IsolatedKurrentVolumeRegressionNative.CreateForeignEvent();
}
