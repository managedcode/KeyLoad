using System.Text.Json;
using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadClientNullReadTests
{
    private const string ApiKey = "null-read-test-key";
    private const string StatusPath = "/v1/status";
    private const string JsonContentType = "application/json";
    private const string UnavailableDetail = "The read response is unavailable.";
    private const string Node = "healthy-node";
    private const string PartitionId = "partition";
    private static readonly string[] ExpectedNullablePaths =
    [
        "/v1/documents/get", "/v1/documents/get", "/v1/queues/inspect",
        "/v1/queues/transfers/inspect", "/v1/queues/transfers/receipt",
        "/v1/queues/schedules/inspect", "/v1/queues/sagas/inspect",
        "/v1/blobs/metadata", "/v1/blobs/uploads/info"
    ];

    [Test]
    [Arguments("null")]
    [Arguments("{")]
    public async Task AllNativeAbsentReadsRemainSuccessfulBeforeUnavailableStatusAndFullHealthyContinuation(string unavailable)
    {
        var observation = new KeyLoadClientKestrelObservation();
        try
        { await ExecuteObservedAsync(unavailable, observation); }
        catch (Exception original)
        {
            observation.WriteAndThrow(original);
            throw;
        }
    }

    private static async Task ExecuteObservedAsync(string unavailable, KeyLoadClientKestrelObservation observation)
    {
        var paths = new List<string>();
        var statusCalls = 0;
        var expected = new NodeStatus(Node, Guid.NewGuid(), 1, Node, 3,
            DurabilityProfile.QuorumProcessDurable, true, Environment.ProcessId);
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            context.Response.ContentType = JsonContentType;
            if (context.Request.Path != StatusPath)
            {
                paths.Add(context.Request.Path.Value!);
                await context.Response.WriteAsync("null", context.RequestAborted);
                return;
            }
            if (Interlocked.Increment(ref statusCalls) == 1)
            { await context.Response.WriteAsync(unavailable, context.RequestAborted); }
            else
            { await JsonSerializer.SerializeAsync(context.Response.Body, expected, JsonDefaults.Options, context.RequestAborted); }
        }, observation);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        await VerifyEveryAbsentReadAsync(client, observation, deadline.Token);
        await Assert.That(paths.SequenceEqual(ExpectedNullablePaths)).IsTrue();
        var rejected = await client.StatusAsync(deadline.Token);
        observation.Record(KestrelObservationStage.SdkCompleted);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(rejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(rejected.Problem?.Detail).IsEqualTo(UnavailableDetail);
        var healthy = await client.StatusAsync(deadline.Token);
        observation.Record(KestrelObservationStage.SdkCompleted);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await Assert.That(JsonDefaults.Serialize(healthy.Value!).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(statusCalls).IsEqualTo(2);
    }

    private static async Task VerifyEveryAbsentReadAsync(KeyLoadClient client, KeyLoadClientKestrelObservation observation, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("tenant", "database", "domain", PartitionId);
        var reference = new EntityRef(partition, "documents", "absent");
        var minimum = new CommitToken(Guid.NewGuid(), "literal-atomic-partition", 1, 1);
        var lane = new QueueLaneRef(partition, "queue");
        var other = new QueueLaneRef(partition, "other-queue");
        var blob = new BlobRef(partition, "blobs", "absent");
        var id = Guid.NewGuid();
        await AbsentAsync(client.GetAsync(reference, cancellationToken), observation);
        await AbsentAsync(client.GetAsync(reference, minimum, cancellationToken), observation);
        await AbsentAsync(client.InspectAsync(new(lane, "absent"), cancellationToken), observation);
        await AbsentAsync(client.InspectQueueTransferAsync(new(lane, id), cancellationToken), observation);
        await AbsentAsync(client.InspectQueueTransferReceiptAsync(new(other, lane, id), cancellationToken), observation);
        await AbsentAsync(client.InspectRecurringScheduleAsync(new(lane, id), cancellationToken), observation);
        await AbsentAsync(client.InspectSagaAsync(new(lane, id), cancellationToken), observation);
        await AbsentAsync(client.GetBlobMetadataAsync(new(blob), cancellationToken), observation);
        await AbsentAsync(client.GetBlobUploadInfoAsync(new(blob, id), cancellationToken), observation);
    }

    private static async Task AbsentAsync<T>(Task<Result<T>> pending, KeyLoadClientKestrelObservation observation)
    {
        var result = await pending;
        observation.Record(KestrelObservationStage.SdkCompleted);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsNull();
    }
}
