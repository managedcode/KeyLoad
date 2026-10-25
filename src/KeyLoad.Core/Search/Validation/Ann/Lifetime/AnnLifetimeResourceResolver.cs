using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Search;

internal sealed class AnnLifetimeResourceResolver(AnnReadLifetimeMetadata metadata, AnnSeedWork work, PrincipalRecord targetPrincipal)
{
    internal global::ManagedCode.Communication.Result<ResourceDefinition?> Resolve(IKeyValueView raw, PartitionRef partition,
        ResourceDefinition target, ReadExecutionBudget budget)
    {
        work.Check();
        var view = budget.CreateView(raw);
        _ = budget.CaptureResourceLifetimes(view, partition, [target]);
        foreach (var definition in metadata.Definitions)
        {
            work.Charge(checked((long)definition.Name.Length + target.Name.Length));
            if (string.Equals(definition.Name, target.Name, StringComparison.Ordinal))
            {
                if (definition.Kind != target.Kind)
                {
                    return global::ManagedCode.Communication.Result<ResourceDefinition?>.FailNotFound(nameof(Resolve));
                }
                var lifetime = metadata.Coverage.Lifetimes.Single(value => value.ResourceName == definition.Name);
                budget.CaptureAuthenticatedResourceLifetimes(view,
                    new ResourceLifetimeCoverage(partition, [lifetime]), [definition]);
                return global::ManagedCode.Communication.Result<ResourceDefinition?>.Succeed(((ResourceDefinition?)(definition)));
            }
        }
        return global::ManagedCode.Communication.Result<ResourceDefinition?>.FailNotFound(nameof(Resolve));
    }

    internal bool CanSeeInput(DatabaseEngine database, ResourceDefinition target, ResourceDefinition source, string field)
    {
        work.Check();
        return Visible(database, metadata.Principal, source, field)
            && Visible(database, targetPrincipal, target, field);
    }

    private bool Visible(DatabaseEngine database, PrincipalRecord principal, ResourceDefinition resource, string field)
    {
        foreach (var policy in database.Authorization.GetEffectiveFieldPolicies(resource, field))
        {
            work.Charge(checked((long)policy.Path.Length + field.Length));
            if (!database.Authorization.CanReadFieldSchema(principal, policy))
            {
                return false;
            }
        }
        return true;
    }
}
