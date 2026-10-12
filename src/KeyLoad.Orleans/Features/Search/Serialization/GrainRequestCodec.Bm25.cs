namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateBm25CorpusPublication(Guid actor, ReplicatedOperation operation, DateTimeOffset expiry)
        => Issue(Bm25RequestEnvelopes.Publication(database, clock.GetUtcNow(), settings.RequestLifetime,
            actor, operation, expiry));
}
