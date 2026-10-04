using Aspire.Hosting.Testing;
using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "rf3")]
[NotInParallel]
internal sealed class RequestIdReceiptTests(ClusterFixture fixture)
{
    private const string RequestIdHeader = "X-KeyLoad-Request-Id";
    private const int ParallelReadCount = 8;

    [Test]
    public async Task SdkResponsesCarryUniqueActorRequestIdsAcrossStableWriteRetryAndParallelReads()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var recorder = CreateRecorder();
        using var http = new HttpClient(recorder, disposeHandler: false)
        {
            BaseAddress = fixture.App.GetEndpoint("node1", "http"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var client = new KeyLoadClient(http, fixture.AdminKey);
        var partition = new PartitionRef($"integration-{Guid.NewGuid():N}", "database", "request-routing", Guid.NewGuid().ToString("N"));
        await ConfigureResourceAsync(client, partition, timeout.Token);
        await VerifyStableWriteRetryAsync(client, recorder, partition, timeout.Token);
        await VerifyParallelReadsAsync(client, recorder, partition, timeout.Token);
    }

    private static async Task ConfigureResourceAsync(KeyLoadClient client, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var configured = await client.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new("orders", ResourceKind.Collection, "request-routing")), cancellationToken);
        if (!configured.IsSuccess)
        {
            Assert.Fail(configured.Problem?.Detail ?? "The resource configuration call failed.");
        }
    }

    private static async Task VerifyStableWriteRetryAsync(
        KeyLoadClient client, RequestIdResponseRecorder recorder, PartitionRef partition, CancellationToken cancellationToken)
    {
        var stableCommandId = Guid.NewGuid();
        var command = new CommandRequest(stableCommandId, partition,
            [new PutDocument("orders", "order-1", "{\"value\":1}", 0)]);
        var first = await client.CommitAsync(command, cancellationToken);
        if (!first.IsSuccess)
        {
            Assert.Fail(first.Problem?.Detail ?? "The initial SDK write failed.");
        }
        var firstReceipt = recorder.Snapshot().Last();

        var retry = await client.CommitAsync(command, cancellationToken);
        if (!retry.IsSuccess)
        {
            Assert.Fail(retry.Problem?.Detail ?? "The stable-command SDK retry failed.");
        }
        var retryReceipt = recorder.Snapshot().Last();
        await Assert.That(retry.Value!.Token).IsEqualTo(first.Value!.Token);
        var writeIds = new[] { firstReceipt, retryReceipt }.Select(ReadSingleRequestId).ToArray();
        await Assert.That(writeIds[0]).IsNotEqualTo(writeIds[1]);
    }

    private static async Task VerifyParallelReadsAsync(KeyLoadClient client, RequestIdResponseRecorder recorder,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var readStart = recorder.Snapshot().Length;
        var reads = await Task.WhenAll(Enumerable.Range(0, ParallelReadCount)
            .Select(_ => client.GetAsync(new(partition, "orders", "order-1"), cancellationToken)));
        foreach (var read in reads)
        {
            if (!read.IsSuccess)
            {
                Assert.Fail(read.Problem?.Detail ?? "A parallel SDK read failed.");
            }
            await Assert.That(read.Value!.Revision).IsEqualTo(1);
        }

        var parallelReceipts = recorder.Snapshot().Skip(readStart).ToArray();
        await Assert.That(parallelReceipts.Length).IsEqualTo(ParallelReadCount);
        await Assert.That(recorder.Overflowed).IsFalse();

        var parallelIds = parallelReceipts.Select(ReadSingleRequestId).ToArray();
        await Assert.That(parallelIds.Distinct().Count()).IsEqualTo(ParallelReadCount);
        var allIds = recorder.Snapshot().Select(ReadSingleRequestId).ToArray();
        await Assert.That(allIds.Distinct().Count()).IsEqualTo(allIds.Length);
        await Assert.That(allIds.All(IsNonEmptyGuidN)).IsTrue();
        await Assert.That(recorder.Snapshot().All(receipt => (int)receipt.StatusCode is >= 200 and < 300)).IsTrue();
    }

    private static string ReadSingleRequestId(RequestIdReceipt receipt)
    {
        var recognizedPath = receipt.Path is "/v1/commands" or "/v1/admin/resources" or "/v1/documents/get";
        if (receipt.HeaderValues.Length != 1 || !recognizedPath)
        {
            Assert.Fail($"Unexpected response receipt for {receipt.Path}: expected exactly one {RequestIdHeader} header.");
        }
        return receipt.HeaderValues.Single();
    }

    private static bool IsNonEmptyGuidN(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;

    private static RequestIdResponseRecorder CreateRecorder()
    {
        SocketsHttpHandler? transport = new();
        try
        {
            var recorder = new RequestIdResponseRecorder(transport);
            transport = null;
            return recorder;
        }
        finally
        {
            transport?.Dispose();
        }
    }
}
