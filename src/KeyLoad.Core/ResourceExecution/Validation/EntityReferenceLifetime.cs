using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Binds fresh names once and refuses rebinding of persisted canonical entity references.</summary>
internal static class EntityReferenceLifetime
{
    private const int IncarnationCharacters = 64;
    private const string MissingBinding = "The canonical entity reference has no supported lifetime binding.";
    private const string ChangedBinding = "The entity reference belongs to another resource lifetime.";
    private const string MissingResource = "The entity reference resource is unavailable.";

    internal static EntityRef Bind(IKeyValueView view, EntityRef reference, ResourceDefinition definition)
        => Bind(reference, Read(view, reference, definition));

    internal static void RequireStored(IKeyValueView view, EntityRef reference, ResourceDefinition definition)
        => RequireStored(reference, Read(view, reference, definition));

    internal static EntityRef Bind(EntityRef reference, ResourceLifetime authenticatedActual)
    {
        RequireScope(reference, authenticatedActual);
        if (reference.Lifetime is not null)
        {
            RequireStored(reference, authenticatedActual);
            return reference;
        }
        return reference with
        {
            Lifetime = new(authenticatedActual.Incarnation.Value)
        };
    }

    internal static void RequireStored(EntityRef reference, ResourceLifetime authenticatedActual)
    {
        RequireBound(reference);
        RequireScope(reference, authenticatedActual);
        if (reference.Lifetime!.Incarnation != authenticatedActual.Incarnation.Value)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ChangedBinding);
        }
    }

    internal static void RequireBound(EntityRef reference)
    {
        RequireReference(reference);
        if (reference.Lifetime is null || !Digest(reference.Lifetime.Incarnation))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, MissingBinding);
        }
    }

    internal static void RequireAdmitted(EntityRef admitted, EntityRef original, ResourceLifetime authenticatedActual)
    {
        RequireStored(admitted, authenticatedActual);
        if (Bind(original, authenticatedActual) != admitted)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ChangedBinding);
        }
    }

    internal static void RequireDocument(DocumentRecord? record, ResourceLifetime authenticatedActual)
    {
        if (record is not null)
        {
            RequireStored(record.Reference, authenticatedActual);
        }
    }

    private static ResourceLifetime Read(IKeyValueView view, EntityRef reference, ResourceDefinition definition)
    {
        RequireReference(reference);
        return ResourceLifetimeCatalog.Read(view, reference.Partition.TenantId, reference.Partition.DatabaseId,
            reference.Collection, definition) ?? throw Errors.Fail(ErrorCode.NotFound, MissingResource);
    }

    private static void RequireScope(EntityRef reference, ResourceLifetime actual)
    {
        RequireReference(reference);
        ArgumentNullException.ThrowIfNull(actual);
        if (reference.Partition is null || actual.Incarnation is null || !Digest(actual.Incarnation.Value))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, MissingBinding);
        }
        if (actual.Kind != ResourceKind.Collection || reference.Partition.TenantId != actual.TenantId || reference.Partition.DatabaseId != actual.DatabaseId
            || reference.Partition.TransactionDomainId != actual.TransactionDomainId || reference.Collection != actual.ResourceName)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ChangedBinding);
        }
    }

    private static void RequireReference(EntityRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Partition is null)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, MissingBinding);
        }
        DatabaseEngine.ValidatePartition(reference.Partition);
        JsonData.Identifier(reference.Collection);
        JsonData.Identifier(reference.Id);
    }

    private static bool Digest(string value)
        => value is { Length: IncarnationCharacters } && value.All(char.IsAsciiHexDigitLower);
}
