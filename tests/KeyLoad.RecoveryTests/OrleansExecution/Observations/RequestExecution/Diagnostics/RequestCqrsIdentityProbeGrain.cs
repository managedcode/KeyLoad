using System.Security.Claims;
using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.RecoveryTests.Features.OrleansExecution;

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
                : GrainByteCounter.Measure(principalSerializer, principal,
                    routingOptions.Value.MaximumPrincipalBytes, routingOptions, CancellationToken.None);
            var stateBytes = state is null
                ? 0
                : GrainByteCounter.Measure(stateSerializer, state,
                    routingOptions.Value.MaximumContextBytes, routingOptions, CancellationToken.None);
            var started = CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.Started(
                Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(actorRequestId)), sequence: 1);
            var startedBytes = GrainByteCounter.Measure(chunkSerializer, started,
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
