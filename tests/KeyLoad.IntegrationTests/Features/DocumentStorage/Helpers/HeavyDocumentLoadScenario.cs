using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Owns independent public callers, lazy disjoint ingestion and settled reader loops.</summary>
internal sealed class HeavyDocumentLoadScenario(ClusterFixture fixture) : IAsyncDisposable
{
    private readonly List<HttpClient> connections = [];
    private readonly List<McpOfficialClient> official = [];
    private readonly HeavyDocumentLoadOverlap overlap = new();
    private CancellationTokenSource? lifetime;
    private Task? originals;
    private int disposed;
    private const int SdkReaderLane = 0;
    private const int McpReaderLane = 1;
    private const int McpQueryLane = 2;

    internal async Task RunAsync(CancellationToken token)
    {
        var source = await McpDocumentScenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var writers = Enumerable.Range(0, HeavyDocumentLoadProtocol.Writers)
            .Select(_ => Sdk(McpCallerProtocol.Node1)).ToArray();
        var reader = Sdk(McpCallerProtocol.Node2);
        var mcpReader = await ConnectAsync(McpCallerProtocol.Node3, token).ConfigureAwait(false);
        var mcpQuery = await ConnectAsync(McpCallerProtocol.Node1, token).ConfigureAwait(false);
        for (var writer = 0; writer < writers.Length; writer++)
        {
            // Each lane's first record is a stable sentinel included in the exact million total.
            await CreateAsync(writers[writer], source.Partition,
                writer * HeavyDocumentLoadProtocol.RecordsPerWriter, token).ConfigureAwait(false);
        }
        var ownedLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime = ownedLifetime;
        var work = new List<Task>(HeavyDocumentLoadProtocol.Writers + HeavyDocumentLoadProtocol.ReaderLanes);
        for (var writer = 0; writer < writers.Length; writer++)
        {
            var lane = writer;
            work.Add(GuardAsync(() => WriteAsync(writers[lane], source.Partition, lane, ownedLifetime.Token), ownedLifetime));
        }
        work.Add(GuardAsync(() => ReadSdkAsync(reader, source.Partition, ownedLifetime.Token), ownedLifetime));
        work.Add(GuardAsync(() => ReadMcpAsync(mcpReader, source.Partition, ownedLifetime.Token), ownedLifetime));
        work.Add(GuardAsync(() => QueryMcpAsync(mcpQuery, source.Partition, ownedLifetime.Token), ownedLifetime));
        // Guard cancellation stops admission; WhenAll settles every original caller before cleanup.
        var joined = Task.WhenAll(work);
        originals = joined;
        try
        { await joined.ConfigureAwait(false); }
        catch (Exception)
        {
            if (joined.Exception is { } originalFailures)
            { throw originalFailures; }
            throw;
        }
        await overlap.VerifyAsync().ConfigureAwait(false);
        await HeavyDocumentLoadReadback.VerifyAsync(reader, source.Partition, token).ConfigureAwait(false);
    }

    private KeyLoadClient Sdk(string node)
    {
        var connection = McpCallerHttp.Create(fixture, node);
        connections.Add(connection);
        return new(connection, fixture.AdminKey, IntegrationClientOptions.Execution());
    }
    private async Task<McpOfficialClient> ConnectAsync(string node, CancellationToken token)
    {
        var owner = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, token).ConfigureAwait(false);
        official.Add(owner);
        return owner;
    }
    private static async Task GuardAsync(Func<Task> operation, CancellationTokenSource lifetime)
    {
        try
        { await operation().ConfigureAwait(false); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
    private async Task CreateAsync(KeyLoadClient client, PartitionRef partition, int ordinal, CancellationToken token,
        int? lane = null)
    {
        var command = HeavyDocumentLoadProtocol.Command(partition, ordinal);
        var original = client.CommitAsync(command, token);
        if (lane is { } writer)
        { overlap.Original(writer, original); }
        try
        {
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await original.ConfigureAwait(false)).ConfigureAwait(false);
            await HeavyDocumentLoadReadback.VerifyReceiptAsync(receipt, command, ordinal).ConfigureAwait(false);
            overlap.Acknowledge();
        }
        finally { if (lane is { } completedWriter) { overlap.Original(completedWriter, null); } }
    }
    private async Task WriteAsync(KeyLoadClient client, PartitionRef partition, int lane, CancellationToken token)
    {
        await overlap.ArriveAsync(token).ConfigureAwait(false);
        var start = lane * HeavyDocumentLoadProtocol.RecordsPerWriter;
        for (var ordinal = start + 1; ordinal < start + HeavyDocumentLoadProtocol.RecordsPerWriter; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            await CreateAsync(client, partition, ordinal, token, lane).ConfigureAwait(false);
        }
        overlap.CompleteWriter();
    }
    private async Task ReadSdkAsync(KeyLoadClient client, PartitionRef partition, CancellationToken token)
    {
        await overlap.ArriveAsync(token).ConfigureAwait(false);
        while (!overlap.WritersDone)
        {
            token.ThrowIfCancellationRequested();
            var original = client.GetAsync(HeavyDocumentLoadReadback.Sentinel(partition), token);
            // Completion is monotonic: a later incomplete read also existed at the observed incomplete write.
            var concurrent = overlap.HasIncompleteOriginal() && !original.IsCompleted;
            var result = await McpCallerAssertions.SdkSuccessAsync(await original.ConfigureAwait(false)).ConfigureAwait(false);
            await HeavyDocumentLoadReadback.VerifySentinelAsync(result, partition).ConfigureAwait(false);
            if (concurrent)
            { overlap.ReadOverlapped(SdkReaderLane); }
        }
    }
    private async Task ReadMcpAsync(McpOfficialClient owner, PartitionRef partition, CancellationToken token)
    {
        await overlap.ArriveAsync(token).ConfigureAwait(false);
        while (!overlap.WritersDone)
        {
            token.ThrowIfCancellationRequested();
            var original = owner.CallAsync(McpCallerTools.DocumentsGet,
                new GetDocumentRequest(HeavyDocumentLoadReadback.Sentinel(partition)), token);
            var concurrent = overlap.HasIncompleteOriginal() && !original.IsCompleted;
            var result = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await original.ConfigureAwait(false))
                .ConfigureAwait(false);
            await HeavyDocumentLoadReadback.VerifySentinelAsync(result.Value, partition).ConfigureAwait(false);
            if (concurrent)
            { overlap.ReadOverlapped(McpReaderLane); }
        }
    }
    private async Task QueryMcpAsync(McpOfficialClient owner, PartitionRef partition, CancellationToken token)
    {
        await overlap.ArriveAsync(token).ConfigureAwait(false);
        var request = HeavyDocumentLoadReadback.SentinelQuery(partition);
        while (!overlap.WritersDone)
        {
            token.ThrowIfCancellationRequested();
            var original = owner.CallAsync(McpCallerTools.QueryAst, request, token);
            var concurrent = overlap.HasIncompleteOriginal() && !original.IsCompleted;
            var result = await McpCallerAssertions.SuccessAsync<QueryPage>(await original.ConfigureAwait(false))
                .ConfigureAwait(false);
            await HeavyDocumentLoadReadback.VerifySentinelPageAsync(result.Value).ConfigureAwait(false);
            if (concurrent)
            { overlap.ReadOverlapped(McpQueryLane); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        { return; }
        var failures = new List<Exception>();
        if (lifetime is not null)
        { await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false); }
        if (originals is not null)
        { await ServerFailureObserver.ObserveAsync(() => originals, failures).ConfigureAwait(false); }
        foreach (var owner in official)
        { await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        foreach (var connection in connections)
        { ServerFailureObserver.Observe(connection.Dispose, failures); }
        lifetime?.Dispose();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
