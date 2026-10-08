using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses real persisted policies and canonical retired frames before complete healthy cold-owner reads.</summary>
internal static class ControlledDocumentRetiredBridgeDenials
{
    private const string DeniedPrincipalId = "retired-bridge-denied";
    private const string DeniedDocumentJson = "{\"denied\":true}";
    private const long EpochStep = 1;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, CancellationToken token)
    {
        var root = source.Store.Read(view => source.Database.Principal(view,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow()));
        var denied = root with { Id = DeniedPrincipalId, ClusterAdministrator = false, Grants = [] };
        Configure(source, denied, token);
        await PolicyDeniedAsync(source, target, ErrorCode.PermissionDenied, token);
        var revoked = denied with { Revoked = true, PolicyEpoch = checked(denied.PolicyEpoch + EpochStep) };
        Configure(source, revoked, token);
        await PolicyDeniedAsync(source, target, ErrorCode.Unauthenticated, token);
        Configure(source, denied with { PolicyEpoch = checked(revoked.PolicyEpoch + EpochStep) }, token);
        await PolicyDeniedAsync(source, target, ErrorCode.PermissionDenied, token);
        await ChangedFrameAsync(source, target, token);
    }

    private static void Configure(ControlledPartitionMovementNode source, PrincipalRecord principal,
        CancellationToken token)
    {
        var operation = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = source.Journal.Submit(operation, token);
        if (result.Error is { } code)
        { throw Errors.Fail(code, result.SafeDetail!); }
    }

    private static async Task PolicyDeniedAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ErrorCode expected, CancellationToken token)
    {
        var sourceBefore = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetBefore = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var reference = Reference();
        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.TryCaptureControlledDocumentRead(
            DeniedPrincipalId, reference, null, Expiry(source), Work(source, token)));
        await Assert.That(readFailure.Code).IsEqualTo(expected);
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, reference.Partition,
            [new PutDocument(reference.Collection, reference.Id, DeniedDocumentJson,
                ControlledPartitionMovementCorpus.InitialRevision, ExplicitReplacement: true)]);
        var operation = source.Database.CreateNativeOperation(OperationKind.Batch, commandId, DeniedPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var commandFailure = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.TryCaptureControlledDocumentCommand(
            DeniedPrincipalId, operation, Work(source, token)));
        await Assert.That(commandFailure.Code).IsEqualTo(expected);
        await sourceBefore.AssertUnchangedAsync(source);
        await targetBefore.AssertUnchangedAsync(target);
    }

    private static async Task ChangedFrameAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, CancellationToken token)
    {
        var frame = source.Database.CaptureControlledDocumentRead(PhysicalShardCatalogFixture.RootPrincipalId,
            Reference(), null, Expiry(source), Work(source, token));
        var sourceBefore = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetBefore = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var changed = frame with
        {
            Publication = frame.Publication with
            { Placement = frame.Publication.Placement with { Revision = checked(frame.Publication.Placement.Revision + EpochStep) } }
        };
        var sourceFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            source.Database.ValidateControlledDocumentRead(changed, Work(source, token)));
        await Assert.That(sourceFailure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        var receiver = Work(target, token);
        var grant = receiver.CreateReadGrant(target.Database.Limits.MaxQueryReadBytes, target.Database.Limits.MaxScanRecords);
        var targetFailure = Assert.ThrowsExactly<KeyLoadException>(() => target.Database.ReadControlledDocument(
            PhysicalShardCatalogFixture.RootPrincipalId, changed, receiver, grant, token));
        await Assert.That(targetFailure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await sourceBefore.AssertUnchangedAsync(source);
        await targetBefore.AssertUnchangedAsync(target);
        source.Database.ValidateControlledDocumentRead(frame, Work(source, token));
    }

    private static EntityRef Reference() => new(ControlledPartitionMovementCorpus.Partition,
        ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId);

    private static DateTimeOffset Expiry(ControlledPartitionMovementNode source)
        => source.Database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds);

    private static ReadExecutionBudget Work(ControlledPartitionMovementNode owner, CancellationToken token)
        => new(Options.Create(owner.Database.Limits), owner.Database.EvaluationClock, token);
}
