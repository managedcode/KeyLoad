using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class FeedNativeHealthyAdmission
{
    internal static global::ManagedCode.Communication.Result<object> Submit(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, string directory,
        global::ManagedCode.Communication.Commands.Command<DatabaseCommand> operation, bool explicitTime, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return explicitTime ? owner.Apply(operation) : owner.ApplyEmbedded(operation, token);
    }
}
