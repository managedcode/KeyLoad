using System.Text.Json;
using KeyLoad.Core;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.CrashHost.Features.EventStreams;

internal static class SharedDomainCrashOperations
{
    internal static ReplicatedOperation Create<T>(OperationKind kind, Guid id, T payload, DateTimeOffset time, string principal = Writer)
        => new(id, kind, principal, time, JsonSerializer.Serialize(payload, JsonDefaults.Options));

    internal static void Configure(DatabaseEngine database)
    {
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        foreach (var scope in scopes)
        {
            foreach (var resource in scope.Resources)
            {
                _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                    new ConfigureResourceRequest(Tenant, Database, resource), Guid.NewGuid()).Get<ResourceDefinition>();
            }
        }
        var principal = new PrincipalRecord(Writer, Tenant,
            [.. scopes.SelectMany(scope => scope.Resources).Select(resource => new ScopeGrant(Database, resource.Name, Capability.All))], []);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), Guid.NewGuid())
            .Get<PrincipalRecord>();
    }
}
