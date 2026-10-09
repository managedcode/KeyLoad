using KeyLoad.Server.Features.BlobStorage;
using System.Globalization;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentEndpoint : IAsyncDisposable
{
    private const string SiloSeparator = ":";
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private readonly NodeOptions options;
    private readonly RemoteDocumentReceiver receiver;
    private readonly RemoteControlledDocumentReceiver controlled;
    private readonly RemoteControlledBlobReceiver controlledBlob;
    private readonly RemoteDocumentWorkOwner owner;
    private readonly ReplicaMembershipAuthorityAddressPins pins;
    private readonly ReplicaMembershipAuthorityReplayCache replay;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;

    internal RemoteDocumentEndpoint(IOptions<NodeOptions> nodeOptions, RemoteDocumentReceiver receiver,
        RemoteDocumentWorkOwner owner, RemoteControlledDocumentReceiver controlled, RemoteControlledBlobReceiver controlledBlob,
        IOptions<OrleansMembershipOptions> membershipOptions,
        IOptions<GrainRoutingOptions> routingOptions, TimeProvider clock)
    {
        options = nodeOptions.Value;
        this.receiver = receiver;
        this.controlled = controlled;
        this.controlledBlob = controlledBlob;
        this.owner = owner;
        this.clock = clock;
        routing = routingOptions.Value;
        var endpoints = options.MembershipAuthority.AuthorityEndpoints.Select(value =>
            new Uri(value).DnsSafeHost + SiloSeparator + MembershipAuthoritySettingsProtocol.NativeSiloPort
                .ToString(CultureInfo.InvariantCulture)).ToArray();
        pins = new(endpoints, membershipOptions);
        try
        { replay = new(clock, membershipOptions); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { pins.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task HandleAsync(HttpContext context)
    {
        if (!options.MembershipAuthority.RemoteDocumentReads
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Proxy)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            RemoteDocumentWorkOwner.Operation operation;
            try
            { operation = owner.Acquire(Guid.NewGuid()); }
            catch (KeyLoadException error) when (error.Code is ErrorCode.OwnershipLost or ErrorCode.ResourceExhausted)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            using (operation)
            {
                await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(context, operation), failures).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(HttpContext context, RemoteDocumentWorkOwner.Operation operation)
    {
        using var deadline = new CancellationTokenSource(routing.ExecutionLifetime, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
            operation.ShutdownToken, deadline.Token);
        var token = linked.Token;
        {
            var body = await RemoteDocumentWire.ReadRequestAsync(context.Request, token).ConfigureAwait(false);
            using var callerMac = RemoteDocumentMac.FromConfiguredSecret(options.MembershipAuthority.AuthorityPeerSecret);
            if (!callerMac.Verify(body, RemoteDocumentWire.Signature(context.Request.Headers), reply: false))
            { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
            var transport = RemoteDocumentWire.DecodeCall(body);
            if (transport.ControlledBlob is { } blobCall)
            {
                if (transport.Document is not null || transport.Controlled is not null)
                { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
                await RemoteControlledBlobEndpointExecution.ExecuteAsync(context, blobCall,
                    controlledBlob, options, pins, replay, clock, token).ConfigureAwait(false);
                return;
            }
            if ((transport.Document is null) == (transport.Controlled is null))
            { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
            if (transport.Controlled is { } controlledCall)
            {
                await RemoteControlledDocumentEndpointExecution.ExecuteAsync(context, controlledCall,
                    controlled, options, pins, replay, clock, token).ConfigureAwait(false);
                return;
            }
            await ExecuteDocumentCallAsync(context, transport.Document!, token).ConfigureAwait(false);
        }
    }

    private async Task ExecuteDocumentCallAsync(HttpContext context, RemoteDocumentCallV1 call, CancellationToken token)
    {
        receiver.Validate(call, token);
        if (!replay.TryUse(call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var address = SiloAddress.FromParsableString(call.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var voter = Array.IndexOf(options.MembershipAuthority.AuthorityEndpoints, call.CallerVoter);
        await pins.PinCallerAsync(voter, address.Endpoint.Address, token).ConfigureAwait(false);
        var remaining = call.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.Unavailable); }
        using var originalExpiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, originalExpiry.Token);
        RemoteDocumentReplyV1 reply;
        try
        {
            if (call.QueryLeaf is not null)
            {
                var leaf = await receiver.ReadLeafAsync(call, original.Token).ConfigureAwait(false);
                reply = new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), leaf);
            }
            else
            {
                var result = await receiver.ReadAsync(call, original.Token).ConfigureAwait(false);
                reply = new(call.RequestId, call.Nonce, result, null, null, receiver.Discovery());
            }
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { reply = new(call.RequestId, call.Nonce, null, error.Code, error.Message, receiver.Discovery()); }
        var encoded = RemoteDocumentWire.Encode(reply, RemoteDocumentWire.ReplyMaximum(call));
        using var replyMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        context.Response.Headers[RemoteDocumentProtocol.SignatureHeader] = replyMac.Sign(encoded, reply: true);
        context.Response.ContentType = RemoteDocumentProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, original.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        { disposal ??= DisposeCoreAsync(); return new ValueTask(disposal); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(owner.StopAsync, failures).ConfigureAwait(false);
        try
        { pins.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { replay.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
