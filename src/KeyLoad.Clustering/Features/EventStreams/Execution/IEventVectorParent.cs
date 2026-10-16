using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal interface IEventVectorParent
{
    Task<GrainOperationReply> ExecuteAdmitAsync(DecodedGrainRequest original,
        GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken originalCancellationToken);
    Task<EventFeedControlResult?> ReadTailOfferAsync(DecodedGrainRequest original, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken originalCancellationToken);
}
