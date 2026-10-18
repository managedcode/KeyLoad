using ManagedCode.Communication.Commands;

namespace KeyLoad;

/// <summary>Returns the exact original command at its committed canonical sequence.</summary>
/// <param name="Sequence">The sequence atomically persisted with the original command outcome.</param>
/// <param name="Command">An owned snapshot of the original admitted command and prepared business facts.</param>
[Orleans.GenerateSerializer]
public sealed record StoredCommand(
    [property: Orleans.Id(StoredCommand.SequenceField)] long Sequence,
    [property: Orleans.Id(StoredCommand.CommandField)] Command<DatabaseCommand> Command)
{
    internal const uint SequenceField = 0;
    internal const uint CommandField = 1;
}
