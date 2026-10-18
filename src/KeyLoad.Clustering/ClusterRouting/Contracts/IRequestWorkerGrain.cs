using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Executes an authorized database operation on one native stateless worker activation.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
internal interface IRequestWorkerGrain : IGrainWithStringKey
{
    Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, string signedRequest, CancellationToken cancellationToken);
    Task<GrainOperationReply> ExecuteCommandAsync(SiloAddress owner, string signedRequest, string actorKey,
        CancellationToken cancellationToken);
}
