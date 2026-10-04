using System.Net;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortEndpoint : IAsyncDisposable
{
    private readonly WebApplication application;
    private RequestCqrsCohortEndpointState state = RequestCqrsCohortEndpointState.Unavailable;
    private long requests;
    private bool stopped;

    private RequestCqrsCohortEndpoint(WebApplication application, string voterId)
    {
        this.application = application;
        VoterId = voterId;
    }

    internal string VoterId { get; }
    internal Uri Origin { get; private set; } = null!;
    internal long Requests => Interlocked.Read(ref requests);

    internal static async Task<RequestCqrsCohortEndpoint> StartAsync(string voterId, CancellationToken token)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(static options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, 0);
        });
        var application = builder.Build();
        var endpoint = new RequestCqrsCohortEndpoint(application, voterId);
        application.MapGet(ReplicaProtocol.DiscoveryPath, endpoint.HandleAsync);
        try
        {
            await application.StartAsync(token);
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = addresses?.SingleOrDefault();
            if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var origin)
                || origin.Port <= 0)
            {
                throw new InvalidOperationException("The owned Kestrel endpoint did not publish a loopback origin.");
            }

            endpoint.Origin = origin;
            return endpoint;
        }
        catch (Exception startupFailure)
        {
            var failures = new List<Exception> { startupFailure };
            using var cleanup = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound);
            await RequestCqrsCohortCleanup.CaptureAsync(() => application.StopAsync(cleanup.Token), failures);
            await RequestCqrsCohortCleanup.CaptureAsync(() => application.DisposeAsync().AsTask(), failures);
            RequestCqrsCohortCleanup.ThrowIfAny(failures);
            throw;
        }
    }

    internal void SetDiscovery(ReplicaEnvelopeAuthenticator authentication, ReplicaSiloDiscovery discovery)
        => Volatile.Write(ref state, new(authentication, discovery, null, null, false, true));

    internal void SetTamperedPayload()
    {
        var current = Volatile.Read(ref state);
        Volatile.Write(ref state, current with { TamperPayload = true, Available = true });
    }

    internal void SetHeaderGate(RequestCqrsCohortHttpGate gate)
    {
        var current = Volatile.Read(ref state);
        Volatile.Write(ref state, current with { HeaderGate = gate, Available = true });
    }

    internal void SetBodyGate(RequestCqrsCohortHttpGate gate)
    {
        var current = Volatile.Read(ref state);
        Volatile.Write(ref state, current with { BodyGate = gate, Available = true });
    }

    internal void SetUnavailable()
    {
        var current = Volatile.Read(ref state);
        Volatile.Write(ref state, current with { Available = false, HeaderGate = null, BodyGate = null });
    }

    internal async Task StopListeningAsync(CancellationToken token)
    {
        if (stopped)
        {
            return;
        }

        await application.StopAsync(token);
        stopped = true;
    }

    private async Task HandleAsync(HttpContext context)
    {
        Interlocked.Increment(ref requests);
        var selected = Volatile.Read(ref state);
        if (!selected.Available)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!TryReadNonce(context, out var nonce))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        try
        {
            if (selected.HeaderGate is not null)
            {
                await selected.HeaderGate.WaitAsync(context.RequestAborted);
            }

            var payload = NativeSerialization.Serialize(selected.Discovery!);
            var signature = selected.Authentication!.SignDiscovery(payload, nonce);
            if (selected.TamperPayload)
            {
                payload = payload.ToArray();
                payload[0] ^= 1;
            }
            context.Response.Headers[ReplicaTransportProtocol.DiscoverySignatureHeader] = signature;
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = payload.Length;
            if (selected.BodyGate is not null)
            {
                var firstCount = Math.Max(1, payload.Length / 2);
                await context.Response.Body.WriteAsync(payload.AsMemory(0, firstCount), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await selected.BodyGate.WaitAsync(context.RequestAborted);
                await context.Response.Body.WriteAsync(payload.AsMemory(firstCount), context.RequestAborted);
                return;
            }

            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            selected.HeaderGate?.MarkAborted();
            selected.BodyGate?.MarkAborted();
            throw;
        }
    }

    private static bool TryReadNonce(HttpContext context, out Guid nonce)
    {
        nonce = Guid.Empty;
        var values = context.Request.Headers[ReplicaTransportProtocol.HttpNonceHeader];
        return values.Count == 1
            && Guid.TryParseExact(values[0], ReplicaTransportProtocol.NonceFormat, out nonce)
            && nonce != Guid.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        var current = Volatile.Read(ref state);
        current.HeaderGate?.Release();
        current.BodyGate?.Release();
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound);
        var failures = new List<Exception>();
        if (!stopped)
        {
            var failureCount = failures.Count;
            await RequestCqrsCohortCleanup.CaptureAsync(() => application.StopAsync(deadline.Token), failures);
            stopped = failures.Count == failureCount;
        }

        await RequestCqrsCohortCleanup.CaptureAsync(() => application.DisposeAsync().AsTask(), failures);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }
}

internal sealed record RequestCqrsCohortEndpointState(
    ReplicaEnvelopeAuthenticator? Authentication,
    ReplicaSiloDiscovery? Discovery,
    RequestCqrsCohortHttpGate? HeaderGate,
    RequestCqrsCohortHttpGate? BodyGate,
    bool TamperPayload,
    bool Available)
{
    internal static RequestCqrsCohortEndpointState Unavailable { get; } = new(null, null, null, null, false, false);
}

internal sealed class RequestCqrsCohortHttpGate
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource aborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => entered.Task;
    internal Task Aborted => aborted.Task;

    internal async Task WaitAsync(CancellationToken token)
    {
        entered.TrySetResult();
        try
        {
            await released.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            aborted.TrySetResult();
            throw;
        }
    }

    internal void MarkAborted() => aborted.TrySetResult();
    internal void Release() => released.TrySetResult();
}
