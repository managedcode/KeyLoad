using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime.Placement;

namespace KeyLoad.Clustering;

/// <summary>Serializes one atomic partition's routing while the physical replica host owns commit and storage.</summary>
/// <param name="codec">Verifier for the original server-issued exact-byte request.</param>
/// <param name="database">Borrowed canonical database used to reload persisted authority.</param>
/// <param name="borrowCoordinator">Borrows the original node-owned admission and quorum commit boundary.</param>
/// <param name="clock">Runtime system clock for persisted principal expiry.</param>
/// <param name="borrowWorkOwner">Borrows the silo-local admission and cancellation owner for verified commands.</param>
/// <param name="diagnostics">Logs unexpected node failures without serializing exceptions.</param>
/// <param name="services">Native silo services for opt-in physical command routing.</param>
/// <param name="options">The centrally validated signed request lifetime settings.</param>
[Orleans.Placement.Immovable(Orleans.Placement.ImmovableKind.Any)]
[Orleans.GrainType(GrainRoutingProtocol.CommandAlias), Orleans.Placement.PreferLocalPlacement]
public sealed class CommandPartitionGrain(GrainRequestCodec codec, DatabaseEngine database, Func<ICommitCoordinator> borrowCoordinator,
    TimeProvider clock, Func<NativeRequestWorkOwner> borrowWorkOwner, ILogger<CommandPartitionGrain> diagnostics,
    IOptions<GrainRoutingOptions> options, IServiceProvider services)
    : Grain, ICommandPartitionGrain
{
    private readonly GrainCommandExecutor commands = new(database, borrowCoordinator(), clock, options, codec, borrowWorkOwner(), services.GetService<IPhysicalRequestPlacement>(),
        services.GetService<IGrainPartitionMovementSealedOperationObserver>(), services.GetService<Func<INativeOnlineTextMaintenance>>()?.Invoke(), services.GetService<IPlacementIntentPersistence>(), () => services.GetRequiredService<INativeDropTableExecution>());

    /// <inheritdoc />
    /// <param name="signedRequest">The signed request whose partition key must match this actor.</param>
    /// <param name="cancellationToken">Cancellation for barrier, commit and result serialization.</param>
    /// <returns>A safe typed reply for the committed operation or a caller-visible failure.</returns>
    public async Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
    {
        var stage = GrainFailureStage.EnvelopeVerification;
        var requestId = Guid.Empty;
        try
        {
            var request = codec.Verify(signedRequest);
            requestId = request.Envelope.RequestId;
            stage = GrainFailureStage.CapabilityExecution;
            var context = codec.HasPhaseObserver ? ((IGrainBase)this).GrainContext : null;
            var reply = await commands.ExecuteAsync(request, this.GetPrimaryKeyString(), cancellationToken,
                context).ConfigureAwait(true);
            if (reply.Error is { } code)
            {
                GrainFailureDiagnostics.Log(diagnostics, Errors.Fail(code, GrainRoutingProtocol.InvalidRequest),
                    requestId, stage, code);
            }
            if (reply.Error is null && context is not null
                && services.GetService<IGrainActivationMigrationObserver>() is { } migration)
            {
                var target = await migration.PrepareMigrationAsync(GrainRequestProbeIdentity.From(request.Envelope),
                    context, cancellationToken).ConfigureAwait(true);
                if (target is not null)
                {
                    MigrateTo(target, cancellationToken);
                }
            }
            return reply;
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error: error, command: true, diagnostics: diagnostics, requestId: requestId, stage: stage, cancellationToken: cancellationToken, options: options);
        }
    }
    /// <inheritdoc />
    public async Task<GrainOperationReply> PrepareOriginalCommandAsync(string signedRequest,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.Empty;
        var stage = GrainFailureStage.EnvelopeVerification;
        try
        {
            var request = codec.Verify(signedRequest);
            requestId = request.Envelope.RequestId;
            stage = GrainFailureStage.CapabilityExecution;
            var context = codec.HasPhaseObserver ? ((IGrainBase)this).GrainContext : null;
            var reply = await NativeCommandObservationPreparation.ExecuteAsync(database, borrowCoordinator(),
                clock, options, codec, borrowWorkOwner(), services.GetService<IPhysicalRequestPlacement>(),
                services.GetService<ReplicaSiloDiscoveryState>(), request, this.GetPrimaryKeyString(),
                context, () => MigratePreparedAsync(request, context, cancellationToken),
                cancellationToken).ConfigureAwait(true);
            return reply;
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error: error, command: false, diagnostics: diagnostics,
                options: options, requestId: requestId, stage: stage, cancellationToken: cancellationToken);
        }
    }

    private async Task MigratePreparedAsync(DecodedGrainRequest request, IGrainContext? context,
        CancellationToken cancellationToken)
    {
        if (context is not null && services.GetService<IGrainActivationMigrationObserver>() is { } migration)
        {
            var target = await migration.PrepareMigrationAsync(GrainRequestProbeIdentity.From(request.Envelope),
                context, cancellationToken).ConfigureAwait(true);
            if (target is not null)
            {
                MigrateTo(target, cancellationToken);
            }
        }
    }

    private void MigrateTo(SiloAddress target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = RequestContext.Entries.FirstOrDefault(
            static entry => entry.Key == IPlacementDirector.PlacementHintKey);
        try
        {
            RequestContext.Set(IPlacementDirector.PlacementHintKey, target);
            MigrateOnIdle();
        }
        finally
        {
            if (previous.Key is not null)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, previous.Value);
            }
            else
            {
                RequestContext.Remove(IPlacementDirector.PlacementHintKey);
            }
        }
    }
}
