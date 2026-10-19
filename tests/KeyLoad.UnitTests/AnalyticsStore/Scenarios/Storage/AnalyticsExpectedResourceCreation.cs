using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

/// <summary>Builds complete expected references from original creation commands, independently of stored rows and read outputs.</summary>
internal static class AnalyticsExpectedResourceCreation
{
    internal static readonly Guid Anchor = Guid.Parse("1abec060-95a4-42ce-a770-0841285b10aa");
    internal static readonly Guid SharedDocuments = Guid.Parse("1abec060-95a4-42ce-a770-0841285b10ab");
    internal static readonly Guid SharedStore = Guid.Parse("1abec060-95a4-42ce-a770-0841285b10ac");
    internal static readonly Guid Unrelated = Guid.Parse("1abec060-95a4-42ce-a770-0841285b10ad");

    internal static ResourceDefinition Configure(TestDatabase fixture, ResourceDefinition definition, Guid commandId)
        => fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition),
            id: commandId).Get<ResourceDefinition>();

    internal static ResourceLifetime Lifetime(TestDatabase fixture, Guid commandId)
    {
        var original = fixture.ReadStoredOutcome(commandId).PreparedCommand
            ?? throw new InvalidOperationException("The original analytics fixture resource creation is missing.");
        var request = OrleansSerialization.Deserialize<ConfigureResourceRequest>((original).Value!.Input.Span);
        return ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
    }

    internal static EntityRef Bind(TestDatabase fixture, EntityRef literal, Guid commandId)
        => Bind(Lifetime(fixture, commandId), literal);

    internal static EntityRef Bind(ResourceLifetime lifetime, EntityRef literal)
    {
        if (literal.Partition.TenantId != lifetime.TenantId || literal.Partition.DatabaseId != lifetime.DatabaseId
            || literal.Partition.TransactionDomainId != lifetime.TransactionDomainId || literal.Collection != lifetime.ResourceName
            || literal.Lifetime is { } guard && guard.Incarnation != lifetime.Incarnation.Value)
        {
            throw new InvalidOperationException("The expected reference does not belong to its original creation command.");
        }
        return literal with
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }
}
