namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAuthorityFields
{
    internal const uint LogicalPrincipalId = 0;
    internal const uint LogicalPolicyEpoch = 1;
    internal const uint FieldHeaderDigest = 2;
    internal const uint SourceOwner = 3;
    internal const uint DestinationOwner = 4;
    internal const uint TechnicalPrincipalId = 5;
    internal const uint TechnicalPolicyEpoch = 6;
    internal const uint ProofDigest = 7;
}
