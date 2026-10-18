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
    private const string DocumentAbsence = "{\"document\":null}";

    [Test]
    [Arguments("null")]
    [Arguments("{")]
    public async Task UnavailableSuccessfulWriteBodyRetainsUnknownOutcomeStableRetryAndNullableReads(string unavailable)
    {
        var observation = new KeyLoadClientKestrelObservation();
        try
        {
            await ExecuteObservedAsync(unavailable, observation);
        }
        catch (Exception original)
        {
            observation.WriteAndThrow(original);
            throw;
        }
    }

    [Test]
    public async Task CredentialStatusRejectsOldBooleanBodyThenSameCommandReceivesNoDataAcknowledgement()
    {
        var commandId = Guid.NewGuid();
        var credential = new ApiKeyRecord("status-key", "status-principal", new string('a', 64));
        var requests = new List<ConfigureApiKeyRequest>();
        var headers = new List<string?>();
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            requests.Add((await JsonSerializer.DeserializeAsync<ConfigureApiKeyRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted))!);
            headers.Add(context.Request.Headers[CommandIdHeader].SingleOrDefault());
            if (requests.Count == 1)
            {
                context.Response.ContentType = JsonContentType;
                await context.Response.WriteAsync("true", context.RequestAborted);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
        });
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        var unknown = await client.ConfigureApiKeyAsync(commandId, credential);
        await Assert.That(unknown.IsFailed).IsTrue();
        await Assert.That(unknown.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(unknown.Problem?.Detail).IsEqualTo(UnknownDetail);
        var acknowledged = await client.ConfigureApiKeyAsync(commandId, credential);
        await Assert.That(acknowledged.IsSuccess).IsTrue();
        await Assert.That(acknowledged.Problem).IsNull();
        await Assert.That(requests.Count).IsEqualTo(2);
        foreach (var request in requests)
        {
            await Assert.That(JsonDefaults.Serialize(request).SequenceEqual(JsonDefaults.Serialize(new ConfigureApiKeyRequest(credential)))).IsTrue();
        }
        foreach (var header in headers)
        {
            await Assert.That(header).IsEqualTo(commandId.ToString());
        }
    }

    [Test]
    public async Task CredentialStatusCancellationKeepsUnknownOutcomeAndFreshSameIdRequestRemainsHealthy()
    {
        var commandId = Guid.NewGuid();
        var credential = new ApiKeyRecord("cancelled-status-key", "status-principal", new string('a', 64));
        var requests = new List<ConfigureApiKeyRequest>();
        var headers = new List<string?>();
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            requests.Add((await JsonSerializer.DeserializeAsync<ConfigureApiKeyRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted))!);
            headers.Add(context.Request.Headers[CommandIdHeader].SingleOrDefault());
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var cancelled = await client.ConfigureApiKeyAsync(commandId, credential, caller.Token);
        await Assert.That(cancelled.IsFailed).IsTrue();
        await Assert.That(cancelled.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(cancelled.Problem?.Detail).IsEqualTo(UnknownDetail);
        await Assert.That(requests.Count).IsEqualTo(0);
        var healthy = await client.ConfigureApiKeyAsync(commandId, credential);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await Assert.That(healthy.Problem).IsNull();
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(JsonDefaults.Serialize(requests[0]).SequenceEqual(
            JsonDefaults.Serialize(new ConfigureApiKeyRequest(credential)))).IsTrue();
        await Assert.That(headers.Single()).IsEqualTo(commandId.ToString());
    }

    [Test]
    public async Task CredentialStatusPreservesOriginalProblemThenSameIdHealthyNoDataAcknowledgement()
    {
        var commandId = Guid.NewGuid();
        var credential = new ApiKeyRecord("denied-status-key", "status-principal", new string('a', 64));
        var expected = Errors.Problem(ErrorCode.PermissionDenied, "Credential status denied.");
        var requests = new List<ConfigureApiKeyRequest>();
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            requests.Add((await JsonSerializer.DeserializeAsync<ConfigureApiKeyRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted))!);
            if (requests.Count == 1)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = JsonContentType;
                await JsonSerializer.SerializeAsync(context.Response.Body, expected, JsonDefaults.Options, context.RequestAborted);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
        });
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        var refused = await client.ConfigureApiKeyAsync(commandId, credential);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(refused.Problem, JsonDefaults.Options).SequenceEqual(
            JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        var healthy = await client.ConfigureApiKeyAsync(commandId, credential);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await Assert.That(healthy.Problem).IsNull();
        await Assert.That(requests.Count).IsEqualTo(2);
        foreach (var request in requests)
        {
            await Assert.That(JsonDefaults.Serialize(request).SequenceEqual(JsonDefaults.Serialize(new ConfigureApiKeyRequest(credential)))).IsTrue();
        }
    }

    private static async Task ExecuteObservedAsync(string unavailable, KeyLoadClientKestrelObservation observation)
    {
        var partition = new PartitionRef("tenant", "database", "domain", PartitionId);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, Document, Json, ExpectedRevision: 0)]);
        var receipt = new CommitReceipt(command.CommandId, new(Guid.NewGuid(), AtomicPartition, Position, Epoch), [new("document.put", Collection, Document, Revision)], DurabilityProfile.ProcessDurable, 1);
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
                {
                    await context.Response.WriteAsync(unavailable, context.RequestAborted);
                }
                else
                {
                    await JsonSerializer.SerializeAsync(context.Response.Body, receipt, JsonDefaults.Options, context.RequestAborted);
                }
                return;
            }
            _ = await JsonSerializer.DeserializeAsync<GetDocumentRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted);
            if (Interlocked.Increment(ref readCount) == 1)
            {
                await context.Response.WriteAsync(DocumentAbsence, context.RequestAborted);
            }
            else
            {
                await JsonSerializer.SerializeAsync(context.Response.Body, new DocumentReadResult(document), JsonDefaults.Options, context.RequestAborted);
            }
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
        await KeyLoadClientNullWriteReadAssertions.VerifyRequestsAndReadsAsync(client, command, requests, headers, reference, document, deadline.Token);
    }
}
