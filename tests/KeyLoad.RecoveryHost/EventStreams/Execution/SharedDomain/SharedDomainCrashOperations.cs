using System.Text.Json;
using KeyLoad.Core;
using ManagedCode.Communication.Commands;
using static KeyLoad.RecoveryHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryHost.Features.EventStreams;

internal static class SharedDomainCrashOperations
{
    internal static Command<DatabaseCommand> Create<T>(DatabaseEngine database, OperationKind kind, Guid id, T payload, DateTimeOffset time, string principal = Writer)
        => database.CreateCommand(kind, id, principal, time, JsonSerializer.Serialize(payload, JsonDefaults.Options));

    internal static Command<DatabaseCommand>[] Configure(DatabaseEngine database)
    {
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        var creations = new List<Command<DatabaseCommand>>();
        foreach (var scope in scopes)
        {
            foreach (var resource in scope.Resources)
            {
                var original = CrashDatabase.Operation(database, OperationKind.ConfigureResource,
                    new ConfigureResourceRequest(Tenant, Database, resource), Guid.NewGuid());
                _ = database.Apply(original).Get<ResourceDefinition>();
                creations.Add(original);
            }
        }
        var principal = new PrincipalRecord(Writer, Tenant,
            [.. scopes.SelectMany(scope => scope.Resources).Select(resource => new ScopeGrant(Database, resource.Name, Capability.All))], []);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), Guid.NewGuid())
            .Get<PrincipalRecord>();
        return creations.ToArray();
    }
}
