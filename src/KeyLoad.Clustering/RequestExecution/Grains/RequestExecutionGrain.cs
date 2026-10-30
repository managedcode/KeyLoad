using Orleans.Placement;

namespace KeyLoad.Clustering;

/// <summary>Runs synchronous capability work on an independent Orleans scheduler beside its physical storage owner.</summary>
[Immovable(ImmovableKind.Any), PreferLocalPlacement]
public sealed class RequestExecutionGrain(ILocalDatabaseRuntime storage, ILocalSiloDetails local)
    : Grain, IRequestExecutionGrain
{
    /// <inheritdoc />
    public async Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, GrainRequestEnvelope envelope, string? actorKey,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(envelope);
            cancellationToken.ThrowIfCancellationRequested();
            GrainRequestScope.RequireRequestIdentity(envelope, this.GetPrimaryKey());
            GrainIdentityContext.Validate(envelope, this.GetPrimaryKey());
            if (!local.SiloAddress.Equals(owner))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
            return await storage.ExecuteAsync(envelope, actorKey, ((IGrainBase)this).GrainContext,
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            DeactivateOnIdle();
        }
    }
}
