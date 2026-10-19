namespace KeyLoad.RecoveryTests.Features.OrleansExecution;

[global::Orleans.Alias(RequestCqrsTestProtocol.ProbeAlias)]
internal interface IRequestCqrsIdentityProbeGrain : IGrainWithGuidKey
{
    Task<RequestCqrsProbeResult> ValidateAsync(string signedRequest, Guid actorRequestId);
    Task<int> CapabilityCallsAsync();
}
