using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Serializes one atomic partition's routing while the physical replica host owns commit and storage.</summary>
/// <param name="codec">Verifier for the original server-issued exact-byte request.</param>
/// <param name="database">Borrowed canonical database used to reload persisted authority.</param>
/// <param name="coordinator">Node-owned admission and quorum commit boundary.</param>
/// <param name="clock">Runtime system clock for persisted principal expiry.</param>
/// <param name="workOwner">Silo-local admission and cancellation owner for verified commands.</param>
/// <param name="diagnostics">Logs unexpected node failures without serializing exceptions.</param>
/// <param name="services">Native silo services for opt-in physical command routing.</param>
/// <param name="options">The centrally validated signed request lifetime settings.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.CommandAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class CommandPartitionGrain(GrainRequestCodec codec, DatabaseEngine database, ICommitCoordinator coordinator,
    TimeProvider clock, NativeRequestWorkOwner workOwner, ILogger<CommandPartitionGrain> diagnostics,
    IOptions<GrainRoutingOptions> options, IServiceProvider services)
    : Grain, ICommandPartitionGrain
{
    private readonly GrainCommandExecutor commands = new(database, coordinator, clock, options, codec, workOwner, services.GetService<IPhysicalRequestPlacement>());

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
}
