using KeyLoad.Clustering;

namespace KeyLoad.RecoveryTests.OrleansExecution;

[global::Orleans.Alias(RequestCqrsTestProtocol.ProbeAlias)]
[global::Orleans.CodeGeneration.Version(1)]
internal interface IRequestCqrsIdentityProbeGrain : IGrainWithGuidKey
{
    Task<RequestCqrsProbeResult> ValidateAsync(GrainRequestEnvelope signedRequest, Guid actorRequestId);
    Task<int> CapabilityCallsAsync();
}
