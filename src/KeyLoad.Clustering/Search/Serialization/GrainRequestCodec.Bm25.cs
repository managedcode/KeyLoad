using ManagedCode.Communication.Commands;
namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateBm25CorpusPublication(Guid actor, Command<DatabaseCommand> operation, DateTimeOffset expiry)
        => Issue(Bm25RequestEnvelopes.Publication(database, clock.GetUtcNow(), settings.RequestLifetime,
            actor, operation, expiry));
}
