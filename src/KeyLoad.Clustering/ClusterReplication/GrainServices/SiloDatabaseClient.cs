using System.Runtime.CompilerServices;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime.Services;

namespace KeyLoad.Clustering;

/// <summary>Addresses storage services using native Orleans membership addresses.</summary>
public sealed class SiloDatabaseClient(IServiceProvider services) : GrainServiceClient<ISiloDatabaseService>(services)
{
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();

    /// <summary>Borrows the storage service on the exact native silo generation.</summary>
    public ISiloDatabaseService On(SiloAddress address) => GetGrainService(address);
    /// <summary>Uses Orleans for owner addressing and native streaming for the complete operation lifetime.</summary>
    public async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        Guid incarnation, string signedRequest, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var owner = await grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
            .GetReadOwnerAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var frame in On(owner).ExecuteStreamAsync(signedRequest, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }
    /// <summary>Resolves storage through Orleans, keeping partition activation placement separate from open files.</summary>
    public async Task<GrainOperationReply> ExecuteCommandAsync(Guid incarnation, string signedRequest,
        string actorKey, CancellationToken cancellationToken)
    {
        var owner = await grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
            .GetReadOwnerAsync(cancellationToken).ConfigureAwait(false);
        return await On(owner).ExecuteCommandAsync(signedRequest, actorKey, cancellationToken).ConfigureAwait(false);
    }
}
