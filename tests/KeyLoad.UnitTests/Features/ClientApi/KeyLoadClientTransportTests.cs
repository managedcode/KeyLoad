using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadClientTransportTests
{
    private const string ApiKey = "client-api-test-key";
    private const string BearerScheme = "Bearer";
    private const string CommandsPath = "/v1/commands";
    private const string JsonContentType = "application/json";
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private const string AuthorizationHeader = "Authorization";
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
        await using var server = await KestrelServer.StartAsync(async context =>
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
        var firstChunkWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestAborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partialResponse = Encoding.UTF8.GetBytes(PartialNodeJson + new string(PartialBodyCharacter, PartialBodyCharacterCount));
        var requestCount = 0;
        await using var server = await KestrelServer.StartAsync(async context =>
        {
            context.Response.ContentType = JsonContentType;
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                try
                {
                    await context.Response.Body.WriteAsync(partialResponse, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    firstChunkWritten.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    requestAborted.TrySetResult();
                }
                return;
            }

            await JsonSerializer.SerializeAsync(context.Response.Body, Status(NodeAfterCancellation), JsonDefaults.Options,
                context.RequestAborted);
        });

        using var cancellation = new CancellationTokenSource();
        var client = new KeyLoadClient(server.Client, ApiKey);
        var pending = client.StatusAsync(cancellation.Token);
        await firstChunkWritten.Task.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
        await Assert.That(pending.IsCompleted).IsFalse();
        await cancellation.CancelAsync();
        var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
        await requestAborted.Task.WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));

        await Assert.That(cancelled.IsFailed).IsTrue();
        await Assert.That(cancelled.Problem!.ErrorCode).IsEqualTo(ErrorCode.Cancelled.ToString());
        var next = await client.StatusAsync().WaitAsync(TimeSpan.FromSeconds(ServerWaitSeconds));
        await Assert.That(next.IsSuccess).IsTrue();
        await Assert.That(next.Value!.NodeId).IsEqualTo(NodeAfterCancellation);
    }

    [Test]
    public async Task ErrorBodiesArePreservedWhenValidAndBoundedWhenNullMalformedOrOversized()
    {
        var bodyNumber = 0;
        string? commandIdHeader = null;
        await using var server = await KestrelServer.StartAsync(async context =>
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

    private sealed class KestrelServer : IAsyncDisposable
    {
        private readonly WebApplication app;
        private KestrelServer(WebApplication app, Uri baseAddress, ConcurrentQueue<string> authorizationHeaders)
        {
            this.app = app;
            Client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
            AuthorizationHeaders = authorizationHeaders;
        }

        public HttpClient Client { get; }
        public ConcurrentQueue<string> AuthorizationHeaders { get; }

        public static async Task<KestrelServer> StartAsync(RequestDelegate handler)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var authorizationHeaders = new ConcurrentQueue<string>();
            app.Run(async context =>
            {
                if (context.Request.Headers[AuthorizationHeader].SingleOrDefault() is { } authorization)
                {
                    authorizationHeaders.Enqueue(authorization);
                }
                await handler(context);
            });
            await app.StartAsync();
            try
            {
                var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
                return new KestrelServer(app, new Uri(addresses.Addresses.Single()), authorizationHeaders);
            }
            catch (Exception)
            {
                await app.StopAsync();
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
