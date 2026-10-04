using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadClientTransportTests
{
    private const string ApiKey = "client-api-test-key";
    private const string BearerScheme = "Bearer";
    private const string CommandsPath = "/v1/commands";
    private const string JsonContentType = "application/json";
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string TransactionDomainId = "domain";
    private const string PartitionKey = "partition";
    private const string UnavailableDetail = "The server response is unavailable.";
    private const string PreservedDetail = "preserved server detail";
    private const string MalformedJson = "{";
    private const string NullJson = "null";
    private const string PartialNodeJson = "{\"nodeId\":\"partial";
    private const string NodeAfterCancellation = "node-after-cancellation";
    private const char LargeNodeIdCharacter = 'n';
    private const char OversizedBodyCharacter = 'x';
    private const char PartialBodyCharacter = 'p';
    private const int LargeNodeIdLength = 524_288;
    private const int ErrorBodyLimitBytes = 64 * 1024;
    private const int ChunkSize = 64 * 1024;
    private const int PartialBodyCharacterCount = 1024 * 1024;
    private const int ChunkDelayMilliseconds = 5;
    private const int ServerWaitSeconds = 5;

    [Test]
    public async Task StatusReadsLargeChunkedTypedResponseOverRealKestrel()
    {
        var expected = Status(new string(LargeNodeIdCharacter, LargeNodeIdLength));
        var payload = JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options);
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            context.Response.ContentType = JsonContentType;
            for (var offset = 0; offset < payload.Length; offset += ChunkSize)
            {
                var length = Math.Min(ChunkSize, payload.Length - offset);
                await context.Response.Body.WriteAsync(payload.AsMemory(offset, length), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(ChunkDelayMilliseconds, context.RequestAborted);
            }
        });

        var result = await new KeyLoadClient(server.Client, ApiKey).StatusAsync();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(server.AuthorizationHeaders).Contains(BearerScheme + " " + ApiKey);
    }

    [Test]
    public async Task MidBodyCancellationMapsReadFailureAndClientCanSendNextRequest()
    {
        var partialResponse = Encoding.UTF8.GetBytes(PartialNodeJson + new string(PartialBodyCharacter, PartialBodyCharacterCount));
        var response = new MidBodyCancellationResponse(partialResponse, Status(NodeAfterCancellation), ChunkSize);
        await using var server = await KeyLoadClientKestrelServer.StartAsync(response.HandleAsync);

        using var cancellation = new CancellationTokenSource();
        using var nextRequestCancellation = new CancellationTokenSource();
        var client = new KeyLoadClient(server.Client, ApiKey);
        var pending = client.StatusAsync(cancellation.Token);
        Task? nextRequest = null;
        try
        {
            await WaitForFirstChunkAsync(response.FirstChunkWritten.Task, response.HandlerEntered.Task,
                response.HandlerFailure.Task, () => response.Stage);
            await Assert.That(pending.IsCompleted).IsFalse();
            await cancellation.CancelAsync();
            var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            response.ReleaseSecondWrite();
            await WaitForRequestAbortAsync(response);

            await Assert.That(cancelled.IsFailed).IsTrue();
            await Assert.That(cancelled.Problem!.ErrorCode).IsEqualTo(ErrorCode.Cancelled.ToString());
            var nextRequestTask = client.StatusAsync(nextRequestCancellation.Token);
            nextRequest = nextRequestTask;
            var next = await nextRequestTask.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            await Assert.That(next.IsSuccess).IsTrue();
            await Assert.That(next.Value!.NodeId).IsEqualTo(NodeAfterCancellation);
        }
        finally
        {
            response.ReleaseSecondWrite();
            await cancellation.CancelAsync();
            await nextRequestCancellation.CancelAsync();
            await pending.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            if (nextRequest is not null)
            {
                await nextRequest.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            }
            if (response.HandlerEntered.Task.IsCompleted)
            {
                await response.FirstHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            }
            if (response.SecondHandlerEntered.Task.IsCompleted)
            {
                await response.SecondHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            }
        }
    }

    private static async Task WaitForRequestAbortAsync(MidBodyCancellationResponse response)
    {
        try
        {
            var completed = await Task.WhenAny(response.RequestAborted.Task, response.HandlerFailure.Task)
                .WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            if (completed == response.HandlerFailure.Task)
            {
                var failure = await response.HandlerFailure.Task;
                throw new InvalidOperationException($"Kestrel response failed during {failure.Stage}.", failure.Error);
            }

            await response.RequestAborted.Task;
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Timed out waiting for Kestrel request abort; stage: {response.Stage}.", exception);
        }
    }

    private static async Task WaitForFirstChunkAsync(Task firstChunkWritten, Task handlerEntered,
        Task<(FirstRequestStage Stage, Exception Error)> handlerFailure, Func<FirstRequestStage> currentStage)
    {
        try
        {
            var completed = await Task.WhenAny(firstChunkWritten, handlerFailure)
                .WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
            if (completed == handlerFailure)
            {
                var failure = await handlerFailure;
                throw new InvalidOperationException(
                    $"Kestrel first response failed during {failure.Stage}.", failure.Error);
            }
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Timed out waiting for the first Kestrel response chunk; handler entered: {handlerEntered.IsCompleted}; stage: {currentStage()}.",
                exception);
        }
    }

    [Test]
    public async Task ErrorBodiesArePreservedWhenValidAndBoundedWhenNullMalformedOrOversized()
    {
        var bodyNumber = 0;
        string? commandIdHeader = null;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = JsonContentType;
            if (context.Request.Path == CommandsPath)
            {
                commandIdHeader = context.Request.Headers[CommandIdHeader].SingleOrDefault();
                await context.Response.WriteAsync(NullJson, context.RequestAborted);
                return;
            }

            switch (Interlocked.Increment(ref bodyNumber))
            {
                case 1:
                    await JsonSerializer.SerializeAsync(context.Response.Body,
                        Errors.Problem(ErrorCode.OwnershipLost, PreservedDetail), JsonDefaults.Options, context.RequestAborted);
                    break;
                case 2:
                    await context.Response.WriteAsync(MalformedJson, context.RequestAborted);
                    break;
                case 3:
                    await context.Response.WriteAsync(NullJson, context.RequestAborted);
                    break;
                default:
                    var oversized = new byte[ErrorBodyLimitBytes + 1];
                    Array.Fill(oversized, (byte)OversizedBodyCharacter);
                    await context.Response.Body.WriteAsync(oversized, context.RequestAborted);
                    break;
            }
        });

        var client = new KeyLoadClient(server.Client, ApiKey);
        await AssertBoundedErrorBodiesAsync(client);

        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, new(TenantId, DatabaseId, TransactionDomainId, PartitionKey), []);
        var write = await client.CommitAsync(command);
        await Assert.That(write.Problem!.ErrorCode).IsEqualTo(ErrorCode.UnknownWriteOutcome.ToString());
        await Assert.That(write.Problem.Detail).IsEqualTo(UnavailableDetail);
        await Assert.That(commandIdHeader).IsEqualTo(commandId.ToString());
    }

    private static async Task AssertBoundedErrorBodiesAsync(KeyLoadClient client)
    {
        var valid = await client.StatusAsync();
        await Assert.That(valid.Problem!.Detail).IsEqualTo(PreservedDetail);

        foreach (var result in new[] { await client.StatusAsync(), await client.StatusAsync(), await client.StatusAsync() })
        {
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
            await Assert.That(result.Problem.Detail).IsEqualTo(UnavailableDetail);
        }
    }

    private static NodeStatus Status(string nodeId)
        => new(nodeId, Guid.Empty, 1, nodeId, 3, DurabilityProfile.QuorumProcessDurable, true, Environment.ProcessId);
}
