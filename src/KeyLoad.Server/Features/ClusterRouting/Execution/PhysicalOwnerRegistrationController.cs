using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationController(OrleansNode node, PartitionHost partition,
    PhysicalOwnerProbeClient probes, PhysicalOwnerStartupRequests requests, IOptions<NodeOptions> nodeOptions)
{
    private readonly Lock state = new();
    private Guid? invokedCommand;
    private bool verified;
    private PhysicalOwnerRegistrationStage stage = PhysicalOwnerRegistrationStage.MembershipPrerequisite;

    internal async Task RegisterAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = nodeOptions.Value;
        if (!options.MembershipAuthority.RegisterPhysicalOwners
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable); }
        var initial = await node.MembershipReadyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        SetStage(PhysicalOwnerRegistrationStage.TargetProbes);
        var fingerprint = await probes.VerifyAllAsync(expiresAt, cancellationToken).ConfigureAwait(false);
        SetStage(PhysicalOwnerRegistrationStage.Authentication);
        var administrator = await requests.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        SetStage(PhysicalOwnerRegistrationStage.MembershipRevalidation);
        var final = await node.MembershipReadyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        if (initial.ActiveFingerprint != final.ActiveFingerprint || fingerprint != final.ActiveFingerprint)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        var request = new RegisterPhysicalOwnerV1(PhysicalOwnerDirectoryProtocol.Version,
            PhysicalOwnerDirectoryProtocol.EmptyRevision, control, destination);
        var commandId = PhysicalOwnerRegistrationIdentity.Create(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (state)
        { invokedCommand = commandId; stage = PhysicalOwnerRegistrationStage.Registration; }
        var reply = await requests.RegisterAsync(administrator, request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SetStage(PhysicalOwnerRegistrationStage.DirectoryVerification);
        var directory = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PhysicalOwnerDirectoryV1
            ?? throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.Malformed);
        PhysicalOwnerDirectoryValidation.Validate(directory);
        if (directory.Revision != PhysicalOwnerDirectoryProtocol.RevisionStep
            || !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, control.Owner)
            || !PhysicalOwnerEntryValidation.Same(directory.Owners[FirstOwner], control)
            || !PhysicalOwnerEntryValidation.Same(directory.Owners[SecondOwner], destination))
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.Malformed); }
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var local = partition.Database.ReadPhysicalOwnerDirectory(administrator.Id);
        if (!NativeSerialization.Serialize(local).AsSpan().SequenceEqual(NativeSerialization.Serialize(directory)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        lock (state)
        { verified = true; }
    }

    private void SetStage(PhysicalOwnerRegistrationStage value)
    {
        lock (state)
        { stage = value; }
    }

    internal (PhysicalOwnerRegistrationStage Stage, bool DeadlineCancelled, bool StoppingCancelled) ObserveFailure(
        CancellationTokenSource deadline, CancellationTokenSource stopping)
    {
        lock (state)
        { return (stage, deadline.IsCancellationRequested, stopping.IsCancellationRequested); }
    }

    private const int FirstOwner = 0;
    private const int SecondOwner = 1;

    internal PhysicalOwnerRegistrationObservation Observe()
    {
        lock (state)
        { return new(invokedCommand, verified); }
    }
}
