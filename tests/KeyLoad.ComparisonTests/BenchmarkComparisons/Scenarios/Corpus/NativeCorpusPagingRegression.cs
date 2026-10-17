using System.Diagnostics;
using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StackExchange.Redis.Profiling;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class NativeCorpusPagingRegression
{
    private const int CorpusCount = 7;
    private const int ConfiguredPageCapacity = 3;
    private const int ExpectedNativePages = 3;
    private const int FirstIndex = 0;
    private const int NodeOrdinalOffset = 1;
    private const int SingleWorkItem = 1;
    private const int NoWarmup = 0;
    private const int VectorDimensions = 8;
    private const string GuidFormat = "N";
    private const string HttpEndpoint = "http";
    private const string NodePrefix = "node";
    private const string RedisPrimary = "primary";
    private const string RedisReplicaPrefix = "replica";
    private const string RedisImage = "docker.io/library/redis:8.4.0@" + BenchmarkResources.RedisDigest;
    private const string RedisKeyPrefix = "keyload-benchmark:";
    private const string PrefixSeparator = ":";
    private const string MissingRedisConnection = "Native paging regression requires the actual Redis connection.";
    private const string HttpActivitySource = "System.Net.Http";
    private const string HttpRequestActivity = "System.Net.Http.HttpRequestOut";
    private const string HttpUrlTag = "url.full";
    private const string QueryAstRoute = "/v1/query/ast";
    private const string PagingActivity = "NativeCorpusPagingRegression";
    private const string NativeMultiGetCommand = "MGET";

    // AC-CQ-034: use the already running selected topology, never a replacement transport.
    internal static async Task VerifyKeyLoadAsync(DistributedApplication app, string adminKey, int nodeCount,
        CancellationToken cancellationToken)
    {
        var clients = new HttpClient?[nodeCount];
        var transferred = false;
        try
        {
            for (var index = FirstIndex; index < nodeCount; index++)
            {
                clients[index] = app.CreateHttpClient(NodePrefix + (index + NodeOrdinalOffset).ToString(CultureInfo.InvariantCulture), HttpEndpoint);
            }
            var peers = clients.Select(client => client!).ToArray();
            await using var target = new KeyLoadTarget(peers[FirstIndex], adminKey, Guid.NewGuid().ToString(GuidFormat),
                NativeExecutionPolicyFixture.Lifecycle(), ConfiguredExecution(), NativeExecutionPolicyFixture.ReadDiagnostics(), NativeExecutionPolicyFixture.Admission(),
                ComparisonClientOptions.Execution(), ComparisonClientOptions.Translation(), peers: peers, expectedNodes: nodeCount);
            transferred = true;
            var corpus = CreateCorpus();
            await target.InitializeAsync(corpus, cancellationToken);
            await using var session = await target.OpenSessionAsync(cancellationToken);
            await VerifyHttpPagesAsync(session, peers[FirstIndex].BaseAddress!, corpus, cancellationToken);
            await VerifyCancellationAndHealthyReadAsync(session, corpus, cancellationToken);
        }
        finally
        {
            if (!transferred)
            {
                foreach (var client in clients)
                {
                    client?.Dispose();
                }
            }
        }
    }

    internal static async Task VerifyRedisAsync(DistributedApplication app, int nodeCount, CancellationToken cancellationToken)
    {
        var connectionString = await app.GetConnectionStringAsync(RedisPrimary, cancellationToken)
            ?? throw new InvalidOperationException(MissingRedisConnection);
        var replicas = new List<string>();
        for (var index = NodeOrdinalOffset; index < nodeCount; index++)
        {
            replicas.Add(await app.GetConnectionStringAsync(RedisReplicaPrefix + index.ToString(CultureInfo.InvariantCulture), cancellationToken)
                ?? throw new InvalidOperationException(MissingRedisConnection));
        }
        var runId = Guid.NewGuid().ToString(GuidFormat);
        var topology = ComparisonTopologies.FromNodeCount(nodeCount);
        var execution = ConfiguredExecution();
        var lifecycle = NativeExecutionPolicyFixture.Lifecycle();
        await using var target = new RedisTarget(connectionString, runId, RedisImage, lifecycle, execution, NativeExecutionPolicyFixture.ReadDiagnostics(), topology, [.. replicas]);
        var corpus = CreateCorpus();
        await target.InitializeAsync(corpus, cancellationToken);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(RedisReplicaProof.CreateOptions(connectionString));
        cancellationToken.ThrowIfCancellationRequested();
        await RedisReplicaProof.VerifyWorkerPrimaryAsync(connection, topology, cancellationToken);
        var profile = new ProfilingSession();
        connection.RegisterProfiler(() => profile);
        await using var session = new RedisComparisonSession(connection, RedisKeyPrefix + runId + PrefixSeparator,
            topology, CorpusCount, lifecycle, execution);
        await VerifyReadbackAsync(session, corpus, cancellationToken);
        await Assert.That(profile.FinishProfiling().Count(command => command.Command == NativeMultiGetCommand)).IsEqualTo(ExpectedNativePages);
        await VerifyCancellationAndHealthyReadAsync(session, corpus, cancellationToken);
    }

    private static async Task VerifyHttpPagesAsync(IComparisonSession session, Uri endpoint, BenchmarkDataset corpus,
        CancellationToken cancellationToken)
    {
        using var operation = new Activity(PagingActivity);
        operation.SetIdFormat(ActivityIdFormat.W3C);
        operation.Start();
        var observedPages = FirstIndex;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HttpActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == HttpRequestActivity && activity.TraceId == operation.TraceId
                    && activity.GetTagItem(HttpUrlTag) is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    && uri.Authority == endpoint.Authority && uri.AbsolutePath == QueryAstRoute)
                {
                    Interlocked.Increment(ref observedPages);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        await VerifyReadbackAsync(session, corpus, cancellationToken);
        await Assert.That(Volatile.Read(ref observedPages)).IsEqualTo(ExpectedNativePages);
    }

    private static async Task VerifyReadbackAsync(IComparisonSession session, BenchmarkDataset corpus,
        CancellationToken cancellationToken)
    {
        var rows = new List<FoundDocument>();
        await foreach (var row in session.ReadCorpusAsync(cancellationToken))
        {
            rows.Add(row);
        }
        await Assert.That(rows.Count).IsEqualTo(CorpusCount);
        await Assert.That(rows.Select(row => row.Id).SequenceEqual(corpus.Documents.Select(document => document.Id), StringComparer.Ordinal)).IsTrue();
        for (var index = FirstIndex; index < CorpusCount; index++)
        {
            await Assert.That(rows[index].Json).IsEqualTo(corpus.Documents[index].Json);
        }
    }

    private static async Task VerifyCancellationAndHealthyReadAsync(IComparisonSession session, BenchmarkDataset corpus,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using (var cursor = session.ReadCorpusAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token))
        {
            await Assert.That(await cursor.MoveNextAsync()).IsTrue();
            await Assert.That(BenchmarkDataset.SameDocument(cursor.Current, corpus.Documents[FirstIndex])).IsTrue();
            await cancellation.CancelAsync();
            await Assert.That(async () => await cursor.MoveNextAsync()).Throws<OperationCanceledException>();
        }
        await Assert.That(BenchmarkDataset.SameDocument(await session.ReadAsync(corpus.Documents[FirstIndex], cancellationToken),
            corpus.Documents[FirstIndex])).IsTrue();
    }

    private static IOptions<NativeComparisonExecutionOptions> ConfiguredExecution()
    {
        var execution = NativeExecutionPolicyFixture.Read();
        execution.Value.ReadbackBatchCapacity = ConfiguredPageCapacity;
        execution.Value.Validate();
        return execution;
    }

    private static BenchmarkDataset CreateCorpus()
        => new(NativeExecutionPolicyFixture.Workload(new ComparisonOptions
        {
            Documents = CorpusCount,
            Operations = SingleWorkItem,
            Warmup = NoWarmup,
            Repetitions = SingleWorkItem,
            Concurrency = SingleWorkItem,
            Dimensions = VectorDimensions,
            TopK = ConfiguredPageCapacity,
            GraphVertices = CorpusCount,
            GraphFanOut = SingleWorkItem,
            GraphDepth = SingleWorkItem
        }));
}
