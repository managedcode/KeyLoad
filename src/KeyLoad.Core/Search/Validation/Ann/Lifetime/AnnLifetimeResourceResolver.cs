using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal sealed class AnnLifetimeResourceResolver(AnnReadLifetimeMetadata metadata, AnnSeedWork work, PrincipalRecord targetPrincipal)
{
    internal ResourceDefinition? Resolve(DatabaseEngine database, IKeyValueView raw, PartitionRef partition,
        ResourceDefinition target, ReadExecutionBudget budget)
    {
        work.Check();
        var view = budget.CreateView(raw);
        _ = budget.CaptureResourceLifetimes(database, view, partition, [target]);
        foreach (var definition in metadata.Definitions)
        {
            work.Charge(checked((long)definition.Name.Length + target.Name.Length));
            if (string.Equals(definition.Name, target.Name, StringComparison.Ordinal))
            {
                if (definition.Kind != target.Kind)
                {
                    return null;
                }
                var lifetime = metadata.Coverage.Lifetimes.Single(value => value.ResourceName == definition.Name);
                budget.CaptureAuthenticatedResourceLifetimes(database, view,
                    new ResourceLifetimeCoverageV1(partition, [lifetime]), [definition]);
                return definition;
            }
        }
        return null;
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
