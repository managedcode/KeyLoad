using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Derives active creation identity from the original canonical operation without changing its fingerprint.</summary>
internal static class ResourceLifetimeIdentity
{
    private const string IncarnationDomain = "keyload:resource-incarnation";
    private const string InvalidIdentity = "The persisted resource lifetime identity is invalid.";
    private const int DigestCharacters = 64;

    internal static ResourceLifetime Create(Command<DatabaseCommand> original, string tenantId,
        string databaseId, ResourceDefinition definition)
    {
        RequireScope(tenantId, databaseId, definition);
        if (original is null || original.CommandId == Guid.Empty
            || original.Value!.Kind is not (OperationKind.ConfigureResource or OperationKind.CreateTable)
            || original.Value.Input.IsEmpty)
        {
            throw Invalid();
        }
        RequireIdentifier(original.UserId!);
        var fingerprint = DatabaseCommandFingerprint.Compute(original);
        var incarnation = Compute(tenantId, databaseId, definition.Name, fingerprint);
        return new(tenantId, databaseId, definition.Name, definition.TransactionDomainId,
            definition.Kind, new(incarnation), fingerprint);
    }

    internal static void Validate(ResourceLifetime lifetime)
    {
        if (lifetime is null)
        {
            throw Invalid();
        }
        Validate(lifetime, lifetime.TenantId, lifetime.DatabaseId,
            new(lifetime.ResourceName, lifetime.Kind, lifetime.TransactionDomainId));
    }

    internal static void Validate(ResourceLifetime lifetime, string tenantId,
        string databaseId, ResourceDefinition definition)
    {
        RequireScope(tenantId, databaseId, definition);
        if (lifetime is null || lifetime.TenantId != tenantId || lifetime.DatabaseId != databaseId
            || lifetime.ResourceName != definition.Name || lifetime.TransactionDomainId != definition.TransactionDomainId
            || lifetime.Kind != definition.Kind || !IsDigest(lifetime.CreationFingerprint)
            || lifetime.Incarnation is null || !IsDigest(lifetime.Incarnation.Value))
        {
            throw Invalid();
        }
        var expected = Compute(tenantId, databaseId, definition.Name, lifetime.CreationFingerprint);
        if (lifetime.Incarnation.Value != expected)
        {
            throw Invalid();
        }
    }

    private static void RequireScope(string tenantId, string databaseId, ResourceDefinition definition)
    {
        if (definition is null || !Enum.IsDefined(definition.Kind))
        {
            throw Invalid();
        }
        RequireIdentifier(tenantId);
        RequireIdentifier(databaseId);
        RequireIdentifier(definition.Name);
        RequireIdentifier(definition.TransactionDomainId);
    }

    private static void RequireIdentifier(string value)
    {
        try
        {
            JsonData.Identifier(value);
        }
        catch (KeyLoadException)
        {
            throw Invalid();
        }
    }

    private static string Compute(string tenantId, string databaseId, string resourceName, string fingerprint)
    {
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode(
                IncarnationDomain, tenantId, databaseId, resourceName, fingerprint)));
        }
        catch (KeyLoadException)
        {
            throw Invalid();
        }
    }

    private static bool IsDigest(string value)
        => value is { Length: DigestCharacters }
            && value.All(char.IsAsciiHexDigitLower);

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Corruption, InvalidIdentity);
}
