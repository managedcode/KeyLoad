using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.DatabaseComposition;

/// <summary>Captures the actual catalog identity before an operation without reading its document result.</summary>
internal static class DatabaseCompositionExpectedReferences
{
    private const int IncarnationCharacters = 64;
    private const string FreshInputRequired = "Capture the expected lifetime from the original name-only input before execution.";
    private const string CatalogRequired = "The literal composition fixture requires its actual configured collection lifetime.";

    internal static EntityRef Capture(TestDatabase fixture, EntityRef fresh)
        => fixture.Store.Read(view => Capture(view, fresh));

    internal static EntityRef Capture(IKeyValueView view, EntityRef fresh)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(fresh);
        if (fresh.Lifetime is not null)
        {
            throw new InvalidOperationException(FreshInputRequired);
        }
        var partition = fresh.Partition;
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            partition.TenantId, partition.DatabaseId, fresh.Collection));
        if (definition is not { Kind: ResourceKind.Collection }
            || definition.TransactionDomainId != partition.TransactionDomainId)
        {
            throw new InvalidOperationException(CatalogRequired);
        }
        var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
            fresh.Collection, definition);
        if (lifetime is null || lifetime.TenantId != partition.TenantId
            || lifetime.DatabaseId != partition.DatabaseId || lifetime.TransactionDomainId != partition.TransactionDomainId
            || lifetime.ResourceName != fresh.Collection || lifetime.Kind != ResourceKind.Collection
            || lifetime.Incarnation.Value is not { Length: IncarnationCharacters } incarnation
            || !incarnation.All(char.IsAsciiHexDigitLower))
        {
            throw new InvalidOperationException(CatalogRequired);
        }
        return fresh with
        {
            Lifetime = new(incarnation)
        };
    }
}
