using System.Security.Claims;
using KeyLoad.Clustering;
using ManagedCode.Orleans.Identity.Core.Constants;

namespace KeyLoad.ProcessRecoveryTests.OrleansExecution;

[global::Orleans.GrainType(RequestCqrsTestProtocol.ProbeAlias)]
internal sealed class RequestCqrsIdentityProbeGrain(
    GrainRequestCodec codec,
    RequestCqrsCapabilityLedger ledger)
    : Grain, IRequestCqrsIdentityProbeGrain
{
    public Task<int> CapabilityCallsAsync() => Task.FromResult(ledger.Count);

    public Task<RequestCqrsProbeResult> ValidateAsync(GrainRequestEnvelope signedRequest, Guid actorRequestId)
    {
        try
        {
            var decoded = codec.DecodeRequest(signedRequest, actorRequestId);
            GrainIdentityContext.Validate(decoded.Envelope, actorRequestId);
            var calls = ledger.RecordCall();
            var state = RequestContext.Get(GrainRequestStreamProtocol.ContextKey) as GrainRequestContextState;
            var principal = RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) as ClaimsPrincipal;
            return Task.FromResult(new RequestCqrsProbeResult(true, null, principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                state?.RequestId ?? Guid.Empty, state?.CommandId ?? Guid.Empty, calls));
        }
        catch (KeyLoadException error)
        {
            return Task.FromResult(new RequestCqrsProbeResult(false, error.Code, null, Guid.Empty, Guid.Empty,
                ledger.Count));
        }
    }
}
