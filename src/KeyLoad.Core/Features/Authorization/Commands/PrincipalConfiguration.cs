using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int CredentialVerifierCharacters = 64;
    private const string PolicyEpochMustAdvanceMessage = "A policy update must advance its epoch.";
    private const string CredentialSha256RequiredMessage = "An API key requires a SHA-256 verifier.";
    private const string InvalidConfiguredCredentialVerifierMessage = "The credential verifier is invalid.";

    private static OperationResult ExecuteConfigurePrincipal(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var principal = Payload<ConfigurePrincipalRequest>(operation).Principal;
        ClusterPrincipalPolicy.RequireMutablePrincipal(principal.Id);
        ValidatePrincipalStructure(principal);
        JsonData.Identifier(principal.Id);
        JsonData.Identifier(principal.TenantId);
        var previous = transaction.GetRecord<PrincipalRecord>(KeySpace.Principal(principal.Id));
        if (previous is not null && principal.PolicyEpoch <= previous.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, PolicyEpochMustAdvanceMessage);
        }
        RequireNoMovementGrant(transaction, principal.Id);
        transaction.PutRecord(KeySpace.Principal(principal.Id), principal);
        return Result(principal);
    }

    private static void RequireNoMovementGrant(IKeyValueView view, string principalId)
    {
        if (PartitionMoveGrantStorage.Outstanding(view, principalId) > PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.PolicyBusy); }
    }

    private OperationResult ExecuteConfigureApiKey(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var credential = Payload<ConfigureApiKeyRequest>(operation).ApiKey;
        ClusterPrincipalPolicy.RequirePublicCredential(credential.PrincipalId);
        if (credential.Verifier.Length != CredentialVerifierCharacters || !credential.Verifier.All(char.IsAsciiHexDigit))
        {
            throw Errors.Fail(ErrorCode.Validation, CredentialSha256RequiredMessage);
        }
        JsonData.Identifier(credential.Id);
        Principal(transaction, credential.PrincipalId, operation.EvaluatedAt);
        if (credential.Verifier.Length != CredentialVerifierCharacters || !credential.Verifier.All(Uri.IsHexDigit))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidConfiguredCredentialVerifierMessage);
        }
        RequireNoMovementGrant(transaction, credential.PrincipalId);
        if (transaction.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(credential.Id)) is { } previous)
        { RequireNoMovementGrant(transaction, previous.PrincipalId); }
        transaction.PutRecord(KeySpace.ApiKey(credential.Id), credential);
        return Result(true);
    }
}
