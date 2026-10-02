using KeyLoad.Core;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

/// <summary>Serializes one atomic partition's routing while the physical replica host owns commit and storage.</summary>
/// <param name="codec">Verifier for the original server-issued exact-byte request.</param>
/// <param name="database">Borrowed canonical database used to reload persisted authority.</param>
/// <param name="coordinator">Node-owned admission and quorum commit boundary.</param>
/// <param name="clock">Runtime system clock for persisted principal expiry.</param>
/// <param name="diagnostics">Logs unexpected node failures without serializing exceptions.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.CommandAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class CommandPartitionGrain(GrainRequestCodec codec, DatabaseEngine database, ICommitCoordinator coordinator,
    TimeProvider clock, ILogger<CommandPartitionGrain> diagnostics) : Grain, ICommandPartitionGrain
{
    private readonly GrainCommandExecutor commands = new(database, coordinator, clock);

    /// <inheritdoc />
    /// <param name="signedRequest">The signed request whose partition key must match this actor.</param>
    /// <param name="cancellationToken">Cancellation for barrier, commit and result serialization.</param>
    /// <returns>A safe typed reply for the committed operation or a caller-visible failure.</returns>
    public async Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
    {
        try
        {
            var request = codec.Verify(signedRequest);
            return await commands.ExecuteAsync(request, this.GetPrimaryKeyString(), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error, true, diagnostics);
        }
    }
}
