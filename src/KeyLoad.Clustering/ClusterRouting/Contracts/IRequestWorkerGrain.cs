using ManagedCode.Communication.CQRS;
using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Executes an authorized database operation on one native stateless worker activation.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
internal interface IRequestWorkerGrain : IGrainWithStringKey
{
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken);
    Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken);
}
