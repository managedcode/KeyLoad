using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class CredentialVerifierR53State(CredentialSubjectReplacementState original)
{
    internal string Secret { get; } = original.First.Identity.Credential.Id
        + CredentialVerifierR53Protocol.SecretSeparator + Guid.NewGuid().ToString(CredentialVerifierR53Protocol.SecretFormat);
    internal Guid ExpireId { get; } = Guid.NewGuid();
    internal Guid ReplaceId { get; } = Guid.NewGuid();
    internal ApiKeyRecord Expired { get; } = original.First.Identity.Credential with
    { ExpiresAt = TimeProvider.System.GetUtcNow() - McpCallerProtocol.ExpiredOffset };
    internal ApiKeyRecord Replacement { get; set; } = original.First.Identity.Credential;
    internal global::ManagedCode.Communication.Result? ExpireResult { get; set; }
    internal global::ManagedCode.Communication.Result? ReplaceResult { get; set; }
    internal CredentialSubjectReplacementImage? Image { get; set; }
    internal OutboxStatus? FirstOutbox { get; set; }
    internal OutboxStatus? SecondOutbox { get; set; }
    internal AtomicPartitionPlacementResolution? FirstPlacement { get; set; }
    internal AtomicPartitionPlacementResolution? SecondPlacement { get; set; }
    internal bool Active { get; set; }
}
