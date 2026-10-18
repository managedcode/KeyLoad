using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Placement;

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
[Immovable(ImmovableKind.Any)]
[GrainType(GrainRoutingProtocol.CommandAlias), PreferLocalPlacement]
public sealed class CommandPartitionGrain(GrainRequestCodec codec, DatabaseEngine database, Func<ICommitCoordinator> borrowCoordinator,
    TimeProvider clock, Func<NativeRequestWorkOwner> borrowWorkOwner, ILogger<CommandPartitionGrain> diagnostics,
    IOptions<GrainRoutingOptions> options, IServiceProvider services)
    : Grain, ICommandPartitionGrain
{
    private readonly GrainCommandExecutor commands = new(database, borrowCoordinator(), clock, options, codec, borrowWorkOwner(), services.GetService<IPhysicalRequestPlacement>(),
        services.GetService<Func<INativeOnlineTextMaintenance>>()?.Invoke(), () => services.GetRequiredService<INativeDropTableExecution>());

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
                services.GetService<SiloStorageIdentityProvider>(), request, this.GetPrimaryKeyString(),
                context, cancellationToken).ConfigureAwait(true);
            return reply;
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error: error, command: false, diagnostics: diagnostics,
                options: options, requestId: requestId, stage: stage, cancellationToken: cancellationToken);
        }
    }
}
