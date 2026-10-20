using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes AuthorizationCommandsPrincipalConfiguration operations against the caller's database context.</summary>
public static class AuthorizationCommandsPrincipalConfiguration
{
    internal const int CredentialVerifierCharacters = 64;
    internal const long InitialPrincipalPolicyEpoch = 1L;
    internal const string InvalidInitialPrincipalEpoch = "An initial principal policy epoch must be positive.";
    internal const string PolicyEpochMustAdvanceMessage = "A policy update must advance its epoch.";
    internal const string CredentialSha256RequiredMessage = "An API key requires a SHA-256 verifier.";
    internal const string InvalidConfiguredCredentialVerifierMessage = "The credential verifier is invalid.";
    internal static Result<object> ExecuteConfigurePrincipal(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var principal = OperationProtocol.Payload<ConfigurePrincipalRequest>(operation).Principal;
        global::KeyLoad.Core.Features.RuntimeJournaling.Identity.RuntimeJournalIdentity.RequireMutablePrincipal(principal.Id);
        ProtocolValidation.ValidatePrincipalStructure(principal);
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
        return OperationProtocol.Result(principal);
    }

    internal static Result<object> ExecuteConfigureApiKey(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var credential = OperationProtocol.Payload<ConfigureApiKeyRequest>(operation).ApiKey;
        global::KeyLoad.Core.Features.RuntimeJournaling.Identity.RuntimeJournalIdentity.RequirePublicCredential(credential.PrincipalId);
        if (credential.Verifier.Length != CredentialVerifierCharacters || !credential.Verifier.All(char.IsAsciiHexDigit))
        {
            throw Errors.Fail(ErrorCode.Validation, CredentialSha256RequiredMessage);
        }

        JsonData.Identifier(credential.Id);
        engine.Principal(transaction, credential.PrincipalId, operation.Timestamp);
        if (credential.Verifier.Length != CredentialVerifierCharacters || !credential.Verifier.All(Uri.IsHexDigit))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidConfiguredCredentialVerifierMessage);
        }

        transaction.PutRecord(KeySpace.ApiKey(credential.Id), credential);
        return OperationProtocol.Result(true);
    }
}
