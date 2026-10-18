using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int CredentialVerifierCharacters = 64;
    private const long InitialPrincipalPolicyEpoch = 1L;
    private const string InvalidInitialPrincipalEpoch = "An initial principal policy epoch must be positive.";
    private const string PolicyEpochMustAdvanceMessage = "A policy update must advance its epoch.";
    private const string CredentialSha256RequiredMessage = "An API key requires a SHA-256 verifier.";
    private const string InvalidConfiguredCredentialVerifierMessage = "The credential verifier is invalid.";

    private static Result<object> ExecuteConfigurePrincipal(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var principal = Payload<ConfigurePrincipalRequest>(operation).Principal;
        global::KeyLoad.Core.Features.ClusterRouting.Identity.RuntimeJournalIdentity.RequireMutablePrincipal(principal.Id);
        ValidatePrincipalStructure(principal);
        JsonData.Identifier(principal.Id);
        JsonData.Identifier(principal.TenantId);
        var previous = transaction.GetRecord<PrincipalRecord>(KeySpace.Principal(principal.Id));
        if (previous is null && principal.PolicyEpoch < InitialPrincipalPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidInitialPrincipalEpoch);
        }
        if (previous is not null && principal.PolicyEpoch <= previous.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, PolicyEpochMustAdvanceMessage);
        }
        transaction.PutRecord(KeySpace.Principal(principal.Id), principal);
        return Result(principal);
    }

    private Result<object> ExecuteConfigureApiKey(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var credential = Payload<ConfigureApiKeyRequest>(operation).ApiKey;
        global::KeyLoad.Core.Features.ClusterRouting.Identity.RuntimeJournalIdentity.RequirePublicCredential(credential.PrincipalId);
        if (credential.Verifier.Length != CredentialVerifierCharacters || !credential.Verifier.All(char.IsAsciiHexDigit))
        {
            throw Errors.Fail(ErrorCode.Validation, CredentialSha256RequiredMessage);
        }
        JsonData.Identifier(credential.Id);
        Principal(transaction, credential.PrincipalId, operation.Timestamp);
        if (credential.Verifier.Length != CredentialVerifierCharacters || !credential.Verifier.All(Uri.IsHexDigit))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidConfiguredCredentialVerifierMessage);
        }
        transaction.PutRecord(KeySpace.ApiKey(credential.Id), credential);
        return Result(true);
    }
}
