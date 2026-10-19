using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests;

internal sealed class TestDatabaseOriginalResourceCommands
{
    private const string OriginalRequired = "The original successful resource creation command is required.";
    private readonly Lock gate = new();
    private readonly Dictionary<(string Tenant, string Database, string Resource), SortedDictionary<long, Creation?>> creations = [];
    private sealed record Creation(Command<DatabaseCommand> Command, ConfigureResourceRequest Request);

    internal static long? Begin(DatabaseEngine database, Command<DatabaseCommand> command)
    {
        if (command.Value!.Kind is not (OperationKind.ConfigureResource or OperationKind.DropTable))
        {
            return null;
        }
        return database.LastApplied;
    }

    internal void Observe(DatabaseEngine database, Command<DatabaseCommand> command, Result<object> result,
        long? previousCanonical)
    {
        if (previousCanonical is null || result.IsFailed)
        {
            return;
        }
        var outcome = RequireOriginal(database, command);
        if (outcome.CanonicalSequence <= previousCanonical.Value)
        {
            return;
        }
        var payload = command.Value!;
        lock (gate)
        {
            if (payload.Kind == OperationKind.DropTable)
            {
                var drop = OrleansSerialization.Deserialize<DropTableRequest>(payload.Input.Span);
                Record((drop.TenantId, drop.DatabaseId, drop.ResourceName), outcome.CanonicalSequence, null);
                return;
            }
            var request = OrleansSerialization.Deserialize<ConfigureResourceRequest>(payload.Input.Span);
            Record((request.TenantId, request.DatabaseId, request.Definition.Name), outcome.CanonicalSequence,
                new(database.OwnCommand(command), request));
        }
    }

    private static StoredOutcome RequireOriginal(DatabaseEngine database, Command<DatabaseCommand> command)
    {
        var outcome = OutcomeStoreOracle.ReadStored(database.Store, command);
        if (outcome is null || outcome.Result.IsFailed || outcome.PreparedCommand.CommandId != command.CommandId
            || outcome.PreparedCommand.UserId != command.UserId
            || outcome.PreparedCommand.Value!.Kind != command.Value!.Kind
            || outcome.PreparedCommand.Value.Fingerprint != command.Value.Fingerprint
            || !outcome.PreparedCommand.Value.Input.Span.SequenceEqual(command.Value.Input.Span))
        {
            throw new InvalidOperationException(OriginalRequired);
        }
        return outcome;
    }

    private void Record((string Tenant, string Database, string Resource) key, long sequence, Creation? creation)
    {
        if (!creations.TryGetValue(key, out var operations))
        {
            operations = [];
            creations.Add(key, operations);
        }
        operations.TryAdd(sequence, creation);
    }

    internal ResourceLifetime Lifetime(string tenant, string database, string resource)
    {
        lock (gate)
        {
            if (!creations.TryGetValue((tenant, database, resource), out var operations))
            {
                throw new InvalidOperationException(OriginalRequired);
            }
            Creation? creation = null;
            foreach (var operation in operations.Values)
            {
                creation = operation is null ? null : creation ?? operation;
            }
            if (creation is null || creation.Request.ExpectedSchemaVersion is not null)
            {
                throw new InvalidOperationException(OriginalRequired);
            }
            return ResourceLifetimeIdentity.Create(creation.Command, creation.Request.TenantId,
                creation.Request.DatabaseId, creation.Request.Definition);
        }
    }

    internal EntityRef Reference(EntityRef literal)
    {
        var lifetime = Lifetime(literal.Partition.TenantId, literal.Partition.DatabaseId, literal.Collection);
        if (literal.Partition.TransactionDomainId != lifetime.TransactionDomainId
            || literal.Lifetime is { } original && original.Incarnation != lifetime.Incarnation.Value)
        {
            throw new InvalidOperationException(OriginalRequired);
        }
        return literal with { Lifetime = new(lifetime.Incarnation.Value) };
    }
}
