using System.Collections.Concurrent;
using System.Text.Json;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class CommittedResponseEndpoint(ConnectionNativeScenario scenario, PrincipalRecord principal,
    bool callerCancellation)
{
    private int count;
    internal ConcurrentQueue<CommandRequest> Requests { get; } = new();
    internal ConcurrentQueue<byte[]> NativeReplies { get; } = new();
    internal TaskCompletionSource FirstChunk { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource FirstJoined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Aborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<Exception> Failure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CommitReceipt? Original { get; private set; }
    internal byte[]? OriginalNativeReply { get; private set; }

    internal async Task HandleAsync(HttpContext context)
    {
        var first = Interlocked.Increment(ref count) == CommittedResponseProtocol.FirstRequest;
        try
        {
            if (count > CommittedResponseProtocol.MaximumRequests ||
                context.Request.Headers[CommittedResponseProtocol.Authorization].SingleOrDefault() !=
                CommittedResponseProtocol.Bearer + CommittedResponseProtocol.ApiKey ||
                scenario.Fixture.Database.Database.Authenticate(CommittedResponseProtocol.ApiKey,
                    scenario.Fixture.Clock.GetUtcNow()) != principal.Id)
            { throw new InvalidOperationException(CommittedResponseProtocol.UnexpectedRequest); }
            var command = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted)
                ?? throw new InvalidOperationException(CommittedResponseProtocol.UnexpectedRequest);
            if (context.Request.Headers[CommittedResponseProtocol.CommandHeader].SingleOrDefault() != command.CommandId.ToString())
            { throw new InvalidOperationException(CommittedResponseProtocol.UnexpectedRequest); }
            Requests.Enqueue(command);
            var requestId = Guid.NewGuid();
            var reply = await scenario.CommandAsync(principal, requestId, command, context.RequestAborted);
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
            context.Response.ContentType = CommittedResponseProtocol.JsonMediaType;
            if (reply.Error is { } error)
            {
                context.Response.StatusCode = Errors.Status(error);
                await JsonSerializer.SerializeAsync(context.Response.Body, Errors.Problem(error,
                    reply.SafeDetail ?? throw new InvalidOperationException(CommittedResponseProtocol.UnexpectedRequest)),
                    JsonDefaults.Options, context.RequestAborted);
                return;
            }
            NativeReplies.Enqueue(reply.Payload.ToArray());
            var receipt = ConnectionNativeAssertions.Value<CommitReceipt>(reply);
            if (first)
            { Original = receipt; OriginalNativeReply = reply.Payload.ToArray(); await InterruptAsync(context, receipt); }
            else
            { await JsonSerializer.SerializeAsync(context.Response.Body, receipt, JsonDefaults.Options, context.RequestAborted); }
        }
        catch (OperationCanceledException) when (first && FirstChunk.Task.IsCompletedSuccessfully
            && context.RequestAborted.IsCancellationRequested)
        { Aborted.TrySetResult(); }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { Failure.TrySetResult(error); throw; }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { Failure.TrySetResult(error); throw; }
        finally
        { if (first) { FirstJoined.TrySetResult(); } }
    }

    private async Task InterruptAsync(HttpContext context, CommitReceipt receipt)
    {
        using var registration = context.RequestAborted.Register(() => Aborted.TrySetResult());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonDefaults.Options);
        await context.Response.Body.WriteAsync(bytes.AsMemory(0, CommittedResponseProtocol.FirstChunkBytes), context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        FirstChunk.TrySetResult();
        await Release.Task.WaitAsync(context.RequestAborted);
        if (!callerCancellation)
        { context.Abort(); await Aborted.Task.WaitAsync(context.RequestAborted); }
    }
}
