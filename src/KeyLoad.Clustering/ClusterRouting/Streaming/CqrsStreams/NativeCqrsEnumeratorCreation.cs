using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal readonly record struct NativeCqrsEnumeratorCreation(
    IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? Enumerator, Exception? Error);
