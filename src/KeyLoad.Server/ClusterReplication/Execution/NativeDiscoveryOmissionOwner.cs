using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class NativeDiscoveryOmissionOwner : IReplicaNativeDiscoveryObserver
{
    private readonly NativeDiscoveryOmissionFiles files;
    private readonly IHttpContextAccessor accessor;
    private readonly string localVoter;

    internal NativeDiscoveryOmissionOwner(IOptions<NodeOptions> node, IOptions<RequestProbeExecutionOptions> options,
        IHttpContextAccessor accessor)
    {
        if (!node.Value.NativeDiscoveryOmission.Enabled)
        {
            throw Invalid();
        }
        this.accessor = accessor;
        localVoter = node.Value.PublicEndpoint;
        files = new(node, options);
    }

    internal IGrainRequestPurposeObserver? PurposeObserver
    {
        get; private set;
    }

    internal void AttachPurposeObserver(IOptions<RequestProbeExecutionOptions> options)
    {
        if (files.ReadPurposeEnrollment() is null)
        {
            PurposeObserver = null;
            return;
        }
        files.RequirePurposeReceiver(localVoter);
        PurposeObserver = new NativeSignedPurposeObserver(files, accessor, localVoter, options);
    }

    internal ReplicaDiscoveryProbe CreateBorrowedProbe() => new(BeforeSend, Verified);

    private void BeforeSend(ReplicaNativeDiscoveryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!NativeDiscoveryOmissionContext.HasForeground(accessor))
        {
            return;
        }
        var selected = files.PeekArm();
        if (selected is null || selected.Value.SourceVoter != localVoter
            || selected.Value.TargetVoter != request.DestinationVoter)
        {
            return;
        }
        if (request.CallerVoter != localVoter)
        {
            throw Invalid();
        }
        files.Locked(() =>
        {
            var arm = files.Arm();
            if (arm is null)
            {
                return false;
            }
            var witness = NativeDiscoveryOmissionContext.Read(accessor, arm.Value, localVoter,
                request.DestinationVoter, request.CallerSiloAddress, request.DestinationSiloAddress, request.Nonce, token);
            if (witness is null)
            {
                return false;
            }
            files.Write(NativeDiscoveryOmissionProtocol.RequestFile, witness.Value);
            return true;
        });
    }

    public ReadOnlyMemory<byte> TransformNativeDiscovery(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery actualState, ReadOnlyMemory<byte> originalPayload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var selected = files.PeekArm();
        if (selected is null || selected.Value.TargetVoter != localVoter)
        {
            return originalPayload;
        }
        var source = files.PeekWitness(NativeDiscoveryOmissionProtocol.RequestFile);
        if (source is null || source.Value.Nonce != request.Nonce)
        {
            return originalPayload;
        }
        return files.Locked(() => TransformLocked(request, actualState, originalPayload, token));
    }

    private ReadOnlyMemory<byte> TransformLocked(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery actualState, ReadOnlyMemory<byte> originalPayload, CancellationToken token)
    {
        var arm = files.Arm();
        var source = files.Witness(NativeDiscoveryOmissionProtocol.RequestFile);
        if (arm is null || source is null || source.Value.Nonce != request.Nonce || arm.Value.TargetVoter != localVoter)
        {
            return originalPayload;
        }
        token.ThrowIfCancellationRequested();
        RequireSource(arm.Value, source.Value);
        if (request.CallerVoter != source.Value.SourceVoter || request.DestinationVoter != localVoter
            || request.CallerSiloAddress != source.Value.SourceSiloAddress
            || request.DestinationSiloAddress != source.Value.TargetSiloAddress
            || actualState.VoterId != localVoter || actualState.SiloAddress != request.DestinationSiloAddress
            || actualState.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw Invalid();
        }
        var omitted = NativeDiscoveryCapabilityOmission.Omit(originalPayload.ToArray(), actualState);
        files.Write(NativeDiscoveryOmissionProtocol.OmittedFile, source.Value with
        {
            Stage = NativeDiscoveryOmissionProtocol.OmittedStage,
            ProducerVoter = localVoter,
            RuntimeJournalReaderContract = StoreReaderContract.Unspecified
        });
        token.ThrowIfCancellationRequested();
        return omitted;
    }

    private void Verified(string voter, Guid nonce, ReplicaSiloDiscovery state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!NativeDiscoveryOmissionContext.HasForeground(accessor))
        {
            return;
        }
        var selected = files.PeekArm();
        if (selected is null || selected.Value.SourceVoter != localVoter || selected.Value.TargetVoter != voter)
        {
            return;
        }
        files.Locked(() =>
        {
            var arm = files.Arm();
            var source = files.Witness(NativeDiscoveryOmissionProtocol.RequestFile);
            if (arm is null || source is null || source.Value.Nonce != nonce || voter != arm.Value.TargetVoter)
            {
                return false;
            }
            RequireSource(arm.Value, source.Value);
            var current = NativeDiscoveryOmissionContext.Read(accessor, arm.Value, localVoter, voter,
                source.Value.SourceSiloAddress, state.SiloAddress, nonce, token);
            var omitted = files.Witness(NativeDiscoveryOmissionProtocol.OmittedFile);
            if (current != source || omitted != (source.Value with
            {
                Stage = NativeDiscoveryOmissionProtocol.OmittedStage,
                ProducerVoter = voter,
                RuntimeJournalReaderContract = StoreReaderContract.Unspecified
            })
                || state.VoterId != voter || state.SiloAddress != source.Value.TargetSiloAddress
                || state.RuntimeJournalReaderContract != StoreReaderContract.Unspecified)
            {
                throw Invalid();
            }
            files.Write(NativeDiscoveryOmissionProtocol.VerifiedFile, source.Value with
            {
                Stage = NativeDiscoveryOmissionProtocol.VerifiedStage,
                ProducerVoter = state.VoterId,
                RuntimeJournalReaderContract = StoreReaderContract.Unspecified
            });
            return true;
        });
    }

    private static void RequireSource(NativeDiscoveryOmissionArm arm, NativeDiscoveryOmissionWitness source)
    {
        if (source.ArmId != arm.ArmId || source.SessionId != arm.SessionId || source.SourceVoter != arm.SourceVoter
            || source.TargetVoter != arm.TargetVoter || source.Route != arm.Route
            || source.Stage != NativeDiscoveryOmissionProtocol.SourceStage || source.CommandId != Guid.Empty
            || source.RuntimeJournalReaderContract is not null || source.ProducerVoter is not null)
        {
            throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() => new(NativeDiscoveryOmissionProtocol.Invalid);
}
