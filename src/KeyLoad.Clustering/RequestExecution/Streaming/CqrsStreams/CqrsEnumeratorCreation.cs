using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal readonly record struct CqrsEnumeratorCreation(
    IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? Enumerator, Exception? Error);
