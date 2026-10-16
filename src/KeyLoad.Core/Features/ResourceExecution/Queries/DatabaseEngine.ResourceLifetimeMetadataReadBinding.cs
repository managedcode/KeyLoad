using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Binds actual authorized catalog metadata without inventing a data partition.</summary>
    internal ResourceDefinition BindAuthorizedReadResourceMetadata(IKeyValueView alreadyChargedView,
        string tenantId, string databaseId, string transactionDomainId, string resourceName,
        ResourceKind expectedKind, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        JsonData.Identifier(tenantId);
        JsonData.Identifier(databaseId);
        JsonData.Identifier(transactionDomainId);
        JsonData.Identifier(resourceName);
        var pair = ReadBoundResourceMetadataPair(alreadyChargedView, tenantId, databaseId,
            transactionDomainId, resourceName, expectedKind);
        ResourceRetirementStorage.RequireOpen(alreadyChargedView, pair.Lifetime, Limits);
        if (alreadyChargedView is IResourceEffectObservation observation)
        {
            observation.ObserveLifetime(pair.Lifetime);
        }
        work.ObserveAuthenticatedResourceLifetimes([pair.Lifetime]);
        return pair.Definition;
    }
}
