using KeyLoad.Server;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Publishes actual response IDs through one instance-owned concurrency handler for each Kestrel host.</summary>
internal sealed class OperationHeaderRequestHandler(bool requireConcurrentRequests)
{
    private const int ParallelRequestCount = 2;
    private const string ResponseNotStarted = "The execution response did not start before identity publication.";
    private const string IdentityNotPublished = "The execution identity was not published.";
    private const string IdentityMismatch = "The published execution identity did not match the request.";
    private readonly TaskCompletionSource<bool>? parallelRequestsReady = requireConcurrentRequests
        ? new(TaskCreationOptions.RunContinuationsAsynchronously)
        : null;
    private int parallelRequestArrivals;

    /// <summary>Handles the four explicit real-network test routes.</summary>
    /// <param name="context">The current execution Kestrel request.</param>
    /// <returns>Completion after the caller-visible response body is written.</returns>
    internal async Task HandleRequestAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Path == OperationHeaderStartTests.BeforeStartPath)
        {
            var id = Guid.NewGuid();
            OperationResponseHeaders.Publish(context, id);
            await WritePublishedIdentityAsync(context, id);
            return;
        }

        if (context.Request.Path == OperationHeaderStartTests.NoExecutionPath)
        {
            await context.Response.WriteAsync(OperationHeaderStartTests.ResponseBody, context.RequestAborted);
            return;
        }

        if (context.Request.Path == OperationHeaderStartTests.StartedBeforeExecutionPath)
        {
            await context.Response.StartAsync(context.RequestAborted);
            if (!context.Response.HasStarted)
            {
                throw new InvalidOperationException(ResponseNotStarted);
            }

            var id = Guid.NewGuid();
            OperationResponseHeaders.Publish(context, id);
            await WritePublishedIdentityAsync(context, id);
            return;
        }

        await WriteParallelIdentityAsync(context);
    }

    /// <summary>Releases a partial concurrency barrier before the owning host stops.</summary>
    public void ReleaseParallelWaiters() => parallelRequestsReady?.TrySetResult(true);

    private async Task WriteParallelIdentityAsync(HttpContext context)
    {
        if (!TryReadParallelId(context.Request.Path, out var id))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        OperationResponseHeaders.Publish(context, id);
        if (parallelRequestsReady is { } barrier)
        {
            if (Interlocked.Increment(ref parallelRequestArrivals) == ParallelRequestCount)
            {
                barrier.TrySetResult(true);
            }

            await barrier.Task.WaitAsync(context.RequestAborted);
        }

        await WritePublishedIdentityAsync(context, id);
    }

    private static bool TryReadParallelId(PathString path, out Guid id)
    {
        id = default;
        return path.Value is { } value
               && value.StartsWith(OperationHeaderStartTests.ParallelPathPrefix, StringComparison.Ordinal)
               && Guid.TryParseExact(value.AsSpan(OperationHeaderStartTests.ParallelPathPrefix.Length),
                   OrleansNodeProtocol.GuidFormat, out id);
    }

    private static async Task WritePublishedIdentityAsync(HttpContext context, Guid expectedId)
    {
        var publishedId = OperationResponseHeaders.RequestId(context)
                          ?? throw new InvalidOperationException(IdentityNotPublished);
        if (publishedId != expectedId || publishedId == Guid.Empty)
        {
            throw new InvalidOperationException(IdentityMismatch);
        }

        await context.Response.WriteAsync(publishedId.ToString(OrleansNodeProtocol.GuidFormat), context.RequestAborted);
    }
}
