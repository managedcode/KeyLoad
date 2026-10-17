using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns preparation work until its exact lifetime is attached to the observation.</summary>
internal struct NativeCommandObservationPreparationWork : IAsyncDisposable
{
    private NativeCapabilityWorkLifetime? work;

    internal readonly NativeCapabilityWorkLifetime Work => work
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal Exception? PrimaryError
    {
        get; set;
    }

    internal void Acquire(NativeRequestWorkOwner owner, Guid requestId,
        IOptions<DatabaseLimits> options, CancellationToken token)
    {
        work = NativeCapabilityWorkLifetime.Acquire(owner, requestId,
            NativeRequestWorkKind.CommandCapability, options, token)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    }

    internal void TransferTo(NativeCommandObservation? observation)
    {
        if (work is not null && observation is not null && observation.OwnsCommandWork(work))
        {
            work = null;
        }
    }

    public readonly ValueTask DisposeAsync()
    {
        if (work is null)
        {
            return ValueTask.CompletedTask;
        }
        work.PrimaryError = PrimaryError;
        return work.DisposeAsync();
    }
}
