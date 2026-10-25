using System.Collections.Immutable;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core;

/// <summary>Prepares consecutive canonical commands once before their copies apply them in one commit.</summary>
public static class StorageOperationBatchPreparation
{
    /// <summary>Prepares the longest prefix of <paramref name="operations"/> that one commit can apply in order.</summary>
    /// <param name="engine">The shared database execution context.</param>
    /// <param name="operations">Verified canonical commands in submission order.</param>
    /// <param name="cancellationToken">Bounds the synchronous retained lookups and preparation.</param>
    /// <returns>For each command of the prefix, its retained original when it already committed here, otherwise its
    /// prepared form. Business time never decreases along the prefix.</returns>
    /// <remarks>Preparation reads the committed state, which does not yet hold the effects of the commands before a
    /// later one. So the prefix stops before a later command that repeats an identity already in the prefix, whose
    /// preparation recorded queue retry decisions, or whose preparation fails: that command is prepared first in a
    /// later batch, against the committed effects of the commands before it, exactly as alone. The first command's
    /// failure propagates.</remarks>
    public static ImmutableArray<Command<DatabaseCommand>> PrepareStorageOperations(this DatabaseEngine engine,
        IReadOnlyList<Command<DatabaseCommand>> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0)
        {
            throw new ArgumentException(StorageOperationBatchApply.EmptyBatch, nameof(operations));
        }

        var prepared = ImmutableArray.CreateBuilder<Command<DatabaseCommand>>(operations.Count);
        var identities = new HashSet<(string?, Guid)>();
        DateTimeOffset? notBefore = null;
        foreach (var operation in operations)
        {
            if (!identities.Add((operation.UserId, operation.CommandId)))
            {
                break;
            }

            Command<DatabaseCommand> command;
            try
            {
                command = engine.ReadStoredCommand(operation, cancellationToken).Value?.Command
                    ?? engine.PrepareStorageOperation(operation, notBefore, cancellationToken);
            }
            catch (Exception error) when (prepared.Count > 0 && CqrsRuntimeFailures.FindFatal(error) is null)
            {
                break;
            }

            if (prepared.Count > 0 && !command.Value!.RetryDecisions.IsEmpty)
            {
                break;
            }

            prepared.Add(command);
            notBefore = notBefore is { } floor && floor > command.Timestamp ? floor : command.Timestamp;
        }

        return prepared.ToImmutable();
    }
}
