using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.CrashHost.Features.Search;

/// <summary>Executes native ANN recovery fixture commands against the actual canonical atomic store.</summary>
internal sealed class NativeAnnCrashCommandOwner(DatabaseEngine database)
{
    internal DatabaseEngine Database => database;

    internal Result<object> Submit(Command<DatabaseCommand> operation, bool explicitTime)
        => explicitTime ? database.Apply(operation)
            : database.ApplyEmbedded(operation, CancellationToken.None);
}
