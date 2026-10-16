namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Retains actual public lifetime witnesses across the original fixture and its cold reopen.</summary>
internal sealed class FunctionalGateReferenceBindings
{
    private const int MaximumResources = 128;
    private readonly Lock gate = new();
    private readonly Dictionary<(string Tenant, string Database, string Domain, string Collection), EntityRefLifetimeV1> bindings = [];

    internal EntityRef Bind(EntityRef reference)
    {
        lock (gate)
        {
            if (!bindings.TryGetValue(Key(reference), out var lifetime)
                || reference.Lifetime is { } supplied && supplied != lifetime)
            {
                throw Invalid();
            }
            return reference with
            {
                Lifetime = lifetime
            };
        }
    }

    internal void Capture(EntityRef actual)
    {
        lock (gate)
        {
            var lifetime = RequireLifetime(actual);
            var key = Key(actual);
            if (bindings.TryGetValue(key, out var existing))
            {
                if (existing != lifetime)
                {
                    throw Invalid();
                }
                return;
            }
            if (bindings.Count >= MaximumResources)
            {
                throw Invalid();
            }
            bindings.Add(key, lifetime);
        }
    }

    internal void ReplaceRecreated(EntityRef original, EntityRef actual)
    {
        lock (gate)
        {
            var key = Key(original);
            var oldLifetime = RequireLifetime(original);
            var freshLifetime = RequireLifetime(actual);
            if (Key(actual) != key || !bindings.TryGetValue(key, out var retained)
                || retained != oldLifetime || oldLifetime == freshLifetime)
            {
                throw Invalid();
            }
            bindings[key] = freshLifetime;
        }
    }

    private static EntityRefLifetimeV1 RequireLifetime(EntityRef reference)
        => reference.Lifetime is { } lifetime && !string.IsNullOrWhiteSpace(lifetime.Incarnation)
            ? lifetime : throw Invalid();

    private static (string Tenant, string Database, string Domain, string Collection) Key(EntityRef reference)
        => (reference.Partition.TenantId, reference.Partition.DatabaseId,
            reference.Partition.TransactionDomainId, reference.Collection);

    private static InvalidOperationException Invalid()
        => new("The functional gate requires its retained actual public resource lifetime.");
}
