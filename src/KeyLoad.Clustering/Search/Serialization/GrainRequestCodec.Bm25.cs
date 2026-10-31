using ManagedCode.Communication.Commands;
namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateBm25CorpusPublication(Guid actor, Command<DatabaseCommand> operation)
        => Issue(Bm25RequestEnvelopes.Publication(this, actor, operation));
}
