namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record CredentialSubjectReplacementState(
    DiscoveryAuthorityState First, DiscoveryAuthorityState Second,
    CommandRequest FirstSeedCommand, CommitReceipt FirstSeedReceipt,
    CommandRequest SecondSeedCommand, CommitReceipt SecondSeedReceipt,
    Guid RebindCommandId, Guid RepairCommandId, string ColdNamespace)
{
    internal string CurrentSecret { get; set; } = First.Identity.Secret;
    internal CredentialVerifierR53State? Verifier { get; set; }
    internal ApiKeyRecord Rebound => First.Identity.Credential with { PrincipalId = Second.Identity.Principal.Id };
    internal global::ManagedCode.Communication.Result? RebindResult { get; set; }
    internal global::ManagedCode.Communication.Result? RepairResult { get; set; }
}

internal sealed record CredentialSubjectReplacementImage(DiscoveryAuthorityImage First, DiscoveryAuthorityImage Second);
