using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal sealed class AnnLifetimeResourceResolver(AnnReadLifetimeMetadata metadata, AnnSeedWork work, PrincipalRecord targetPrincipal)
{
    private const string Invalid = "The target ANN lineage resource lifetime is invalid.";

    internal ResourceDefinition? Resolve(IKeyValueView raw, PartitionRef partition,
        ResourceDefinition target, ReadExecutionBudget budget)
    {
        work.Check();
        _ = ResourceLifetimeCatalog.Read(budget.CreateView(raw), partition.TenantId, partition.DatabaseId, target.Name, target)
            ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        foreach (var definition in metadata.Definitions)
        {
            work.Charge(checked((long)definition.Name.Length + target.Name.Length));
            if (string.Equals(definition.Name, target.Name, StringComparison.Ordinal))
            {
                return definition.Kind == target.Kind ? definition : null;
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
