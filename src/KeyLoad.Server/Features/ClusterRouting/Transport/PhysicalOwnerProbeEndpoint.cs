using System.Globalization;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeEndpoint : IAsyncDisposable
{
    private const int NoLoggingFailures = 0;
    private const int OriginalFailureOrdinal = 0;
    private const string SiloSeparator = ":";
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private readonly NodeOptions options;
    private readonly PhysicalOwnerProbeReceiver receiver;
    private readonly PhysicalOwnerProbeWorkOwner owner;
    private readonly ReplicaMembershipAuthorityAddressPins pins;
    private readonly ReplicaMembershipAuthorityReplayCache replay;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;
    private readonly ILogger<PhysicalOwnerProbeEndpoint> logger;

    internal PhysicalOwnerProbeEndpoint(IOptions<NodeOptions> nodeOptions, PhysicalOwnerProbeReceiver receiver,
        PhysicalOwnerProbeWorkOwner owner, IOptions<OrleansMembershipOptions> membershipOptions,
        IOptions<GrainRoutingOptions> routingOptions, TimeProvider clock, ILogger<PhysicalOwnerProbeEndpoint> logger)
    {
        options = nodeOptions.Value;
        this.receiver = receiver;
        this.owner = owner;
        this.clock = clock;
        this.logger = logger;
        routing = routingOptions.Value;
        var endpoints = options.MembershipAuthority.AuthorityEndpoints.Select(value =>
            new Uri(value).DnsSafeHost + SiloSeparator + MembershipAuthoritySettingsProtocol.NativeSiloPort
                .ToString(CultureInfo.InvariantCulture)).ToArray();
        pins = new(endpoints, membershipOptions);
        replay = new(clock, membershipOptions);
    }

    internal async Task HandleAsync(HttpContext context)
    {
        if (!options.MembershipAuthority.RegisterPhysicalOwners
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Proxy)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ExecuteAdmittedAsync(context, failures), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAdmittedAsync(HttpContext context, List<Exception> failures)
    {
        var entered = false;
        try
        {
            using var operation = owner.Acquire(Guid.NewGuid());
            entered = true;
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(context, operation), failures).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (!entered && error.Code is ErrorCode.OwnershipLost or ErrorCode.ResourceExhausted)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
    }

    private async Task ExecuteAsync(HttpContext context, PhysicalOwnerProbeWorkOwner.Operation operation)
    {
        using var deadline = new CancellationTokenSource(routing.ExecutionLifetime, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
            operation.ShutdownToken, deadline.Token);
        var token = linked.Token;
        var trace = new PhysicalOwnerProbeTrace();
        try
        {
            trace.Stage = PhysicalOwnerProbeStage.Framing;
            var body = await PhysicalOwnerProbeWire.ReadRequestAsync(context.Request, token).ConfigureAwait(false);
            trace.Stage = PhysicalOwnerProbeStage.Mac;
            using var callerMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.MembershipAuthority.AuthorityPeerSecret);
            if (!callerMac.Verify(body, PhysicalOwnerProbeWire.Signature(context.Request.Headers), reply: false))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
            trace.Stage = PhysicalOwnerProbeStage.Decoding;
            var call = PhysicalOwnerProbeWire.DecodeCall(body);
            trace.Stage = PhysicalOwnerProbeStage.CallValidation;
            receiver.Validate(call, token);
            trace.Stage = PhysicalOwnerProbeStage.ReplayAdmission;
            if (!replay.TryUse(call.Nonce))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
            trace.Stage = PhysicalOwnerProbeStage.CallerAddress;
            var address = SiloAddress.FromParsableString(call.CallerSiloAddress);
            if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
            { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
            var voter = Array.IndexOf(options.MembershipAuthority.AuthorityEndpoints, call.CallerVoter);
            trace.Stage = PhysicalOwnerProbeStage.CallerPinning;
            await pins.PinCallerAsync(voter, address.Endpoint.Address, token).ConfigureAwait(false);
            var reply = await receiver.ReadAsync(call, trace, token).ConfigureAwait(false);
            trace.Stage = PhysicalOwnerProbeStage.ReplyEncoding;
            var encoded = PhysicalOwnerProbeWire.Encode(reply);
            using var replyMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.PeerSecret);
            context.Response.Headers[PhysicalOwnerProbeProtocol.SignatureHeader] = replyMac.Sign(encoded, reply: true);
            context.Response.ContentType = PhysicalOwnerProbeProtocol.ContentType;
            context.Response.ContentLength = encoded.Length;
            trace.Stage = PhysicalOwnerProbeStage.ReplyWrite;
            await context.Response.Body.WriteAsync(encoded, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { }
        catch (OperationCanceledException) when (operation.ShutdownToken.IsCancellationRequested || deadline.IsCancellationRequested)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; }
        catch (KeyLoadException error) when (!context.Response.HasStarted && error.Code != ErrorCode.Corruption)
        {
            context.Response.StatusCode = FailureStatus(error.Code);
            RecordFailure(error, trace, deadline.IsCancellationRequested,
                operation.ShutdownToken.IsCancellationRequested, context.Response.StatusCode);
        }
    }

    private static int FailureStatus(ErrorCode code) => code switch
    {
        ErrorCode.Unauthenticated or ErrorCode.PermissionDenied => StatusCodes.Status401Unauthorized,
        ErrorCode.Validation => StatusCodes.Status400BadRequest,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => StatusCodes.Status413PayloadTooLarge,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private void RecordFailure(KeyLoadException error, PhysicalOwnerProbeTrace trace,
        bool deadlineCancelled, bool stoppingCancelled, int responseStatus)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => PhysicalOwnerRegistrationLog.ReceiverFailed(logger, error.Code,
            trace, deadlineCancelled, stoppingCancelled, responseStatus), failures);
        if (failures.Count != NoLoggingFailures)
        { failures.Insert(OriginalFailureOrdinal, error); ServerFailureObserver.ThrowIfAny(failures); }
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
