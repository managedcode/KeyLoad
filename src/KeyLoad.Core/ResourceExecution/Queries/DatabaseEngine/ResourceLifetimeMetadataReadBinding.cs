using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ResourceLifetimeMetadataReadBinding operations against the caller's database context.</summary>
public static class ResourceLifetimeMetadataReadBinding
{
    /// <summary>Binds actual authorized catalog metadata without inventing a data partition.</summary>
    internal static ResourceDefinition BindAuthorizedReadResourceMetadata(IKeyValueView alreadyChargedView, string tenantId, string databaseId, string transactionDomainId, string resourceName, ResourceKind expectedKind, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        JsonData.Identifier(tenantId);
        JsonData.Identifier(databaseId);
        JsonData.Identifier(transactionDomainId);
        JsonData.Identifier(resourceName);
        var pair = ResourceDefinitionBinding.ReadBoundResourceMetadataPair(alreadyChargedView, tenantId, databaseId, transactionDomainId, resourceName, expectedKind);
        if (alreadyChargedView is IResourceEffectObservation observation)
        {
            observation.ObserveLifetime(pair.Lifetime);
        }

        work.ObserveAuthenticatedResourceLifetimes([pair.Lifetime]);
        return pair.Definition;
    }
}
