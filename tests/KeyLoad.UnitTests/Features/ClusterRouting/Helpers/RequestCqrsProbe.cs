using System.Security.Claims;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsTestProtocol
{
    internal const string ProbeAlias = "keyload.test.request-cqrs-probe.v1";
    internal const string ObservationAlias = "keyload.test.request-cqrs-observation.v1";
}

[global::Orleans.Alias(RequestCqrsTestProtocol.ProbeAlias)]
internal interface IRequestCqrsIdentityProbeGrain : IGrainWithGuidKey
{
    Task<RequestCqrsProbeResult> ValidateAsync(string signedRequest, Guid actorRequestId);
    Task<int> CapabilityCallsAsync();
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsTestProtocol.ObservationAlias)]
internal sealed record RequestCqrsProbeResult(
    [property: global::Orleans.Id(0)] bool Accepted,
    [property: global::Orleans.Id(1)] ErrorCode? Error,
    [property: global::Orleans.Id(2)] string? Subject,
    [property: global::Orleans.Id(3)] Guid RequestId,
    [property: global::Orleans.Id(4)] Guid CommandId,
    [property: global::Orleans.Id(5)] long PrincipalBytes,
    [property: global::Orleans.Id(6)] long StateBytes,
    [property: global::Orleans.Id(7)] long StartedChunkBytes,
    [property: global::Orleans.Id(8)] int CapabilityCalls);

internal sealed class RequestCqrsCapabilityLedger
{
    private int calls;

    internal int Count => Volatile.Read(ref calls);

    internal int RecordCall() => Interlocked.Increment(ref calls);
}

[global::Orleans.GrainType(RequestCqrsTestProtocol.ProbeAlias)]
internal sealed class RequestCqrsIdentityProbeGrain(
    GrainRequestCodec codec,
    RequestCqrsCapabilityLedger ledger,
    Serializer<ClaimsPrincipal> principalSerializer,
    Serializer<GrainRequestContextState> stateSerializer,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer,
    IOptions<GrainRoutingOptions> routingOptions)
    : Grain, IRequestCqrsIdentityProbeGrain
{
    public Task<int> CapabilityCallsAsync() => Task.FromResult(ledger.Count);

    public Task<RequestCqrsProbeResult> ValidateAsync(string signedRequest, Guid actorRequestId)
    {
        try
        {
            var decoded = codec.VerifyRequest(signedRequest, actorRequestId);
            GrainIdentityContext.Validate(decoded.Envelope, actorRequestId);
            var calls = ledger.RecordCall();
            var state = RequestContext.Get(GrainRequestStreamProtocol.ContextKey) as GrainRequestContextState;
            var principal = RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) as ClaimsPrincipal;
            var principalBytes = principal is null
                ? 0
                : GrainNativeByteCounter.Measure(principalSerializer, principal,
                    routingOptions.Value.MaximumPrincipalBytes, routingOptions, CancellationToken.None);
            var stateBytes = state is null
                ? 0
                : GrainNativeByteCounter.Measure(stateSerializer, state,
                    routingOptions.Value.MaximumContextBytes, routingOptions, CancellationToken.None);
            var started = CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.Started(
                Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(actorRequestId)), sequence: 1);
            var startedBytes = GrainNativeByteCounter.Measure(chunkSerializer, started,
                routingOptions.Value.MaximumStartedBytes, routingOptions, CancellationToken.None);
            return Task.FromResult(new RequestCqrsProbeResult(true, null, principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                state?.RequestId ?? Guid.Empty, state?.CommandId ?? Guid.Empty, principalBytes, stateBytes,
                startedBytes, calls));
        }
        catch (KeyLoadException error)
        {
            return Task.FromResult(new RequestCqrsProbeResult(false, error.Code, null, Guid.Empty, Guid.Empty,
                0, 0, 0, ledger.Count));
        }
    }
}

internal sealed class RequestCqrsClientContext : IDisposable
{
    private const string StateKey = GrainRequestStreamProtocol.ContextKey;
    private const string PrincipalKey = OrleansIdentityConstants.USER_CLAIMS;
    private readonly bool stateWasPresent;
    private readonly bool principalWasPresent;
    private readonly object? priorState;
    private readonly object? priorPrincipal;
    private bool disposed;

    private RequestCqrsClientContext(ClaimsPrincipal? principal, bool includePrincipal,
        GrainRequestContextState? state, bool includeState, bool presentNullPrincipal)
    {
        stateWasPresent = RequestContext.Keys.Contains(StateKey, StringComparer.Ordinal);
        principalWasPresent = RequestContext.Keys.Contains(PrincipalKey, StringComparer.Ordinal);
        priorState = RequestContext.Get(StateKey);
        priorPrincipal = RequestContext.Get(PrincipalKey);
        if (includeState)
        {
            RequestContext.Set(StateKey, state!);
        }
        else
        {
            RequestContext.Remove(StateKey);
        }

        if (presentNullPrincipal)
        {
            RequestContext.Set(PrincipalKey, null!);
        }
        else if (includePrincipal)
        {
            RequestContext.Set(PrincipalKey, principal!);
        }
        else
        {
            RequestContext.Remove(PrincipalKey);
        }
    }

    internal static RequestCqrsClientContext Set(ClaimsPrincipal? principal, bool includePrincipal,
        GrainRequestContextState? state, bool includeState = true, bool presentNullPrincipal = false)
        => new(principal, includePrincipal, state, includeState, presentNullPrincipal);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Restore(StateKey, stateWasPresent, priorState);
        Restore(PrincipalKey, principalWasPresent, priorPrincipal);
    }

    private static void Restore(string key, bool wasPresent, object? value)
    {
        if (wasPresent)
        {
            RequestContext.Set(key, value!);
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
