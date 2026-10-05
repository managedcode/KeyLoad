using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityEndpoint : IDisposable
{
    private readonly NodeOptions options;
    private readonly ReplicaMembershipAuthorityOwner owner;
    private readonly TimeProvider clock;
    private readonly ReplicaMembershipAuthorityMac callerMac;
    private readonly ReplicaMembershipAuthorityMac authorityMac;
    private readonly ReplicaMembershipAuthorityReplayCache replay;
    private readonly ReplicaMembershipAuthorityOperations operations;
    private readonly ReplicaMembershipAuthorityAddressPins? addressPins;
    private readonly SemaphoreSlim admissions;
    private readonly OrleansMembershipOptions settings;
    private readonly TimeSpan requestTimeout;
    private int disposalState;

    internal ReplicaMembershipAuthorityEndpoint(IOptions<NodeOptions> nodeOptions, ReplicaMembershipAuthorityOwner owner,
        TimeProvider clock, IOptions<OrleansMembershipOptions> membershipOptions,
        IOptions<ReplicaExecutionOptions> executionOptions)
    {
        var options = nodeOptions.Value;
        this.options = nodeOptions.Value;
        this.owner = owner;
        this.clock = clock;
        settings = membershipOptions.Value;
        var execution = executionOptions.Value;
        requestTimeout = execution.CommandTimeout + execution.ReadBarrierTimeout;
        admissions = new(settings.MaximumAdmissions);
        addressPins = options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority
            ? new(options.MembershipAuthority.TrustedGroupSiloEndpoints) : null;
        var callerKey = Decode(options.MembershipAuthority.TrustedGroupPeerSecret);
        var authorityKey = Decode(options.PeerSecret);
        try
        {
            callerMac = new(callerKey);
            authorityMac = new(authorityKey);
            operations = new(nodeOptions, authorityMac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(callerKey);
            CryptographicOperations.ZeroMemory(authorityKey);
        }
        replay = new(clock, membershipOptions);
    }

    internal bool IsDisposed => Volatile.Read(ref disposalState) == 2;

    internal async Task HandleAsync(HttpContext context)
    {
        if (Volatile.Read(ref disposalState) != 0
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority || !owner.IsReady)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
        if (!ValidHttp(context.Request) || !ReplicaMembershipAuthorityHttpHeaders.TryRead(context.Request, out var headers))
        { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        if (!TryTimestamp(headers.Timestamp, out var timestamp) || !FreshTimestamp(timestamp)
            || !ReplicaMembershipAuthorityValidation.ValidNonce(headers.Nonce))
        { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        if (!admissions.Wait(TimeSpan.Zero))
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
        await HandleAdmittedAsync(context, headers).ConfigureAwait(false);
    }

    private async Task HandleAdmittedAsync(HttpContext context, ReplicaMembershipAuthorityRequestHeaders headers)
    {
        ReplicaMembershipAuthorityOwner.Operation? operation = null;
        try
        {
            operation = owner.TryEnter();
            if (operation is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            using var deadline = new CancellationTokenSource(requestTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
                operation.ShutdownToken, deadline.Token);
            await ExecuteAdmittedAsync(context, headers, operation, deadline, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            admissions.Release();
            operation?.Dispose();
        }
    }

    private async Task ExecuteAdmittedAsync(HttpContext context, ReplicaMembershipAuthorityRequestHeaders headers,
        ReplicaMembershipAuthorityOwner.Operation operation, CancellationTokenSource deadline, CancellationToken token)
    {
        ReplicaMembershipAuthorityCallV1? call = null;
        try
        {
            call = await ReadAuthenticatedCallAsync(context, headers, token).ConfigureAwait(false);
            if (call is null)
            { return; }
            var voter = AuthenticateCaller(headers, out var address);
            await addressPins!.PinCallerAsync(voter, address, token).ConfigureAwait(false);
            var reply = await operations.ExecuteAsync(operation.Provider, call, token).ConfigureAwait(false);
            await operations.WriteReplyAsync(context, call, headers.Nonce, reply).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (OperationCanceledException) when (operation.ShutdownToken.IsCancellationRequested)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { await WriteUnavailableAsync(context, headers, call).ConfigureAwait(false); }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (call is null)
            { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            else
            { await operations.WriteReplyAsync(context, call, headers.Nonce, operations.Failed(call, headers.Nonce, error)).ConfigureAwait(false); }
        }
    }

    private async Task<ReplicaMembershipAuthorityCallV1?> ReadAuthenticatedCallAsync(HttpContext context,
        ReplicaMembershipAuthorityRequestHeaders headers, CancellationToken cancellationToken)
    {
        var body = await ReplicaMembershipAuthorityHttpBody.ReadAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (body is null)
        { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return null; }
        if (!callerMac.VerifyRequest(headers.Cluster, headers.AuthorityPhysical, headers.AuthorityIncarnation,
                headers.CallerPhysical, headers.CallerIncarnation, headers.CallerVoter, headers.CallerSilo,
                headers.Timestamp, headers.Nonce, body, headers.Signature))
        { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return null; }
        if (AuthenticateCaller(headers, out _) < 0 || !replay.TryUse(headers.Nonce))
        { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return null; }
        ReplicaMembershipAuthorityCallV1? call = null;
        try
        {
            call = ReplicaMembershipAuthorityCodec.DeserializeCall(body);
            ReplicaMembershipAuthorityValidation.Call(call);
            operations.VerifyCallHeaderBinding(call, headers);
            return call;
        }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (error.Code == ErrorCode.Unauthenticated)
            { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return null; }
            if (call is null || call.RequestId == Guid.Empty)
            { context.Response.StatusCode = StatusCodes.Status400BadRequest; return null; }
            await operations.WriteReplyAsync(context, call, headers.Nonce, operations.Failed(call, headers.Nonce, error))
                .ConfigureAwait(false);
            return null;
        }
    }

    private async Task WriteUnavailableAsync(HttpContext context, ReplicaMembershipAuthorityRequestHeaders headers,
        ReplicaMembershipAuthorityCallV1? call)
    {
        if (call is null)
        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
        var error = Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable);
        await operations.WriteReplyAsync(context, call, headers.Nonce, operations.Failed(call, headers.Nonce, error))
            .ConfigureAwait(false);
    }

    private int AuthenticateCaller(ReplicaMembershipAuthorityRequestHeaders headers, out IPAddress address)
    {
        address = IPAddress.None;
        var trusted = options.MembershipAuthority;
        if (headers.Cluster != options.ClusterId || headers.CallerPhysical != trusted.TrustedGroupPhysicalShardId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat)
            || headers.CallerIncarnation != trusted.TrustedGroupIncarnation.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat)
            || !trusted.TrustedGroupVoterIds.Contains(headers.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(headers.CallerSilo))
        { return -1; }
        var silo = SiloAddress.FromParsableString(headers.CallerSilo);
        if (silo.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { return -1; }
        address = silo.Endpoint.Address;
        return Array.IndexOf(trusted.TrustedGroupVoterIds, headers.CallerVoter);
    }

    private bool FreshTimestamp(long ticks)
        => Math.Abs(clock.GetUtcNow().UtcDateTime.Ticks - ticks) <= settings.ClockSkew.Ticks;

    private static bool TryTimestamp(string value, out long ticks)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
            && ticks > DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
            && ticks.ToString(CultureInfo.InvariantCulture) == value;

    private static bool ValidHttp(HttpRequest request)
        => request.Method == HttpMethods.Post && request.Path == ReplicaMembershipAuthorityProtocol.Path
            && !request.QueryString.HasValue && request.ContentLength is >= 0
            && request.ContentLength <= ReplicaMembershipAuthorityProtocol.MaximumRequestBytes
            && request.Headers.TryGetValue(Microsoft.Net.Http.Headers.HeaderNames.ContentLength, out var lengths) && lengths.Count == 1
            && request.ContentType == ReplicaMembershipAuthorityProtocol.ContentType
            && !request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.TransferEncoding)
            && !request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.ContentEncoding);

    private static byte[] Decode(string? secret)
        => secret is null ? [] : Convert.FromBase64String(secret);

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref disposalState, 1, 0) != 0)
        { return; }
        callerMac.Dispose();
        authorityMac.Dispose();
        addressPins?.Dispose();
        admissions.Dispose();
        replay.Dispose();
        Volatile.Write(ref disposalState, 2);
    }
}
