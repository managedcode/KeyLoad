using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Authenticates configured A movement traffic before any receiver-local native capability is issued.</summary>
internal sealed partial class PartitionMovementPeerAdmission : IDisposable
{
    internal const int EmptyBodyBytes = 0;
    internal const string NonceFormat = "N";
    internal const string InvalidProof = "The partition movement peer proof is invalid.";
    private readonly NodeOptions options;
    private readonly DatabaseEngine database;
    private readonly ReplicaConfiguration configuration;
    private readonly GrainRoutingOptions routing;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly PartitionMovementExecutionOptions execution;
    private readonly TimeProvider clock;
    private readonly PartitionMovementReplayCache replay;
    private readonly PartitionMovementReplayCache nativeReplay;

    internal PartitionMovementPeerAdmission(IOptions<NodeOptions> options, DatabaseEngine database, IOptions<ReplicaConfiguration> configuration,
        IOptions<GrainRoutingOptions> routing, IOptions<OrleansMembershipOptions> membership,
        IOptions<PartitionMovementExecutionOptions> execution, TimeProvider clock)
    {
        this.options = options.Value;
        this.database = database;
        this.configuration = configuration.Value;
        this.options.Validate();
        this.configuration.Validate();
        if (this.configuration.Incarnation != database.Store.Identity.Incarnation
            || this.configuration.LocalId != this.options.PublicEndpoint
            || !this.configuration.VoterIds.SequenceEqual(this.options.Peers, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof);
        }
        this.routing = routing.Value;
        this.membership = membership;
        this.execution = execution.Value;
        this.clock = clock;
        replay = new(clock, membership);
        try
        {
            nativeReplay = new(clock, membership);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                replay.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<PartitionMovementTransportRequest> VerifyAsync(ReadOnlyMemory<byte> originalBody,
        string signature, CancellationToken cancellationToken)
        => PartitionMovementPeerFrameAdmission.VerifyAsync(this, originalBody, signature, cancellationToken);

    internal PartitionMovementMac CreateControlMac()
        => PartitionMovementControlMacFactory.Create(options, configuration, database);

    public void Dispose()
        => PartitionMovementPeerReplayShutdown.Dispose(replay, nativeReplay);

    internal PartitionMovementExecutionOptions Execution => execution;
    internal NodeOptions Options => options;
    internal DatabaseEngine Database => database;
    internal ReplicaConfiguration Configuration => configuration;
    internal TimeProvider Clock => clock;
    internal GrainRoutingOptions Routing => routing;
    internal IOptions<OrleansMembershipOptions> Membership => membership;
    internal PartitionMovementReplayCache Replay => replay;

    internal Task<PartitionMovementOutcomeTransportRequest> VerifyOutcomeAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementOutcomePeerAdmission.VerifyOutcomeAsync(this, body, signature, token);

    internal Task<PartitionMovementTransferDataRequest> VerifyTransferDataAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementTransferDataPeerAdmission.VerifyTransferDataAsync(this, body, signature, token);

    internal Task<PartitionMovementReceiverIssueRequest> VerifyReceiverIssueAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementReceiverIssuePeerAdmission.VerifyReceiverIssueAsync(this, body, signature, token);

    internal Task<PartitionMovementReceiverIssueQuery> VerifyReceiverIssueQueryAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementReceiverIssuePeerAdmission.VerifyReceiverIssueQueryAsync(this, body, signature, token);

    internal Task<PartitionMovementRetireCancellationRequest> VerifyRetireCancellationAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementRetireCancellationPeerAdmission.VerifyRetireCancellationAsync(this, body, signature, token);

    internal Task<PartitionMovementRetireCancellationQuery> VerifyRetireCancellationQueryAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementRetireCancellationPeerAdmission.VerifyRetireCancellationQueryAsync(this, body, signature, token);
}
