using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.ResourceExecution;

/// <summary>Derives an active resource's incarnation from the command that created it.</summary>
internal static class ResourceLifetimeIdentity
{
    private const string InvalidIdentity = "The persisted resource lifetime identity is invalid.";
    private const string IncarnationFormat = "N";
    internal const int IncarnationCharacters = 32;

    internal static ResourceLifetime Create(Command<DatabaseCommand> original, string tenantId,
        string databaseId, ResourceDefinition definition)
    {
        RequireScope(tenantId, databaseId, definition);
        if (original is null || original.CommandId == Guid.Empty
            || original.Value!.Kind is not (OperationKind.ConfigureResource or OperationKind.CreateTable)
            || original.Value.Input is null)
        {
            throw Invalid();
        }
        RequireIdentifier(original.UserId!);
        return new(tenantId, databaseId, definition.Name, definition.TransactionDomainId,
            definition.Kind, Incarnation(original));
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
            || lifetime.Kind != definition.Kind
            || lifetime.Incarnation is null || !IsIncarnation(lifetime.Incarnation.Value))
        {
            throw Invalid();
        }
    }

    /// <summary>The incarnation a creating command gives its resource.</summary>
    internal static ResourceIncarnation Incarnation(Command<DatabaseCommand> original)
        => new(original.CommandId.ToString(IncarnationFormat));

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

    internal static bool IsIncarnation(string? value)
        => value is { Length: IncarnationCharacters }
            && value.All(char.IsAsciiHexDigitLower);

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Corruption, InvalidIdentity);
}
