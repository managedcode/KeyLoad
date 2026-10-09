using System.Text.Json;
using KeyLoad.Client;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadClientNullWriteTests
{
    private const string ApiKey = "null-write-test-key";
    private const string CommandsPath = "/v1/commands";
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private const string JsonContentType = "application/json";
    private const string UnknownDetail = "The write response is unavailable. Retry the same command ID.";
    private const string Collection = "documents";
    private const string Document = "original";
    private const string Json = """{"value":1}""";
    private const long Revision = 1;
    private const long Position = 7;
    private const long Epoch = 2;
    private const string AtomicPartition = "literal-atomic-partition";
    private const string PartitionId = "partition";

    [Test]
    [Arguments("null")]
    [Arguments("{")]
    public async Task UnavailableSuccessfulWriteBodyRetainsUnknownOutcomeStableRetryAndNullableReads(string unavailable)
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
        var partition = new PartitionRef("tenant", "database", "domain", PartitionId);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, Document, Json, ExpectedRevision: 0)]);
        var receipt = new CommitReceipt(command.CommandId, new(Guid.NewGuid(), AtomicPartition, Position, Epoch),
            [new("document.put", Collection, Document, Revision)], DurabilityProfile.QuorumProcessDurable);
        var reference = new EntityRef(partition, Collection, Document);
        var document = new DocumentResult(reference, Revision, Json, false, []);
        var requests = new List<CommandRequest>();
        var headers = new List<string?>();
        var readCount = 0;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            context.Response.ContentType = JsonContentType;
            if (context.Request.Path == CommandsPath)
            {
                requests.Add((await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
                    JsonDefaults.Options, context.RequestAborted))!);
                headers.Add(context.Request.Headers[CommandIdHeader].SingleOrDefault());
                if (requests.Count == 1)
                { await context.Response.WriteAsync(unavailable, context.RequestAborted); }
                else
                { await JsonSerializer.SerializeAsync(context.Response.Body, receipt, JsonDefaults.Options, context.RequestAborted); }
                return;
            }
            if (Interlocked.Increment(ref readCount) == 1)
            { await context.Response.WriteAsync("null", context.RequestAborted); }
            else
            { await JsonSerializer.SerializeAsync(context.Response.Body, document, JsonDefaults.Options, context.RequestAborted); }
        }, observation);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        var unknown = await client.CommitAsync(command, deadline.Token);
        observation.Record(KestrelObservationStage.SdkCompleted);
        await Assert.That(unknown.IsSuccess).IsFalse();
        await Assert.That(unknown.Value).IsNull();
        await Assert.That(unknown.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(unknown.Problem?.Detail).IsEqualTo(UnknownDetail);
        var retry = await client.CommitAsync(command, deadline.Token);
        observation.Record(KestrelObservationStage.SdkCompleted);
        await Assert.That(retry.IsSuccess).IsTrue();
        await Assert.That(NativeSerialization.Serialize(retry.Value!).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await VerifyRequestsAndReadsAsync(client, command, requests, headers, reference, document, deadline.Token);
    }

    private static async Task VerifyRequestsAndReadsAsync(KeyLoadClient client, CommandRequest command,
        List<CommandRequest> requests, List<string?> headers, EntityRef reference, DocumentResult document, CancellationToken cancellationToken)
    {
        await Assert.That(requests.Count).IsEqualTo(2);
        await Assert.That(headers.Count).IsEqualTo(2);
        foreach (var request in requests)
        {
            await Assert.That(JsonDefaults.Serialize(request).SequenceEqual(JsonDefaults.Serialize(command))).IsTrue();
        }
        foreach (var header in headers)
        { await Assert.That(header).IsEqualTo(command.CommandId.ToString()); }
        var absent = await client.GetAsync(reference, cancellationToken);
        await Assert.That(absent.IsSuccess).IsTrue();
        await Assert.That(absent.Value).IsNull();
        var healthy = await client.GetAsync(reference, cancellationToken);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await Assert.That(NativeSerialization.Serialize(healthy.Value!).SequenceEqual(NativeSerialization.Serialize(document))).IsTrue();
    }
}
