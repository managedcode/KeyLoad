using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportTrial
{
    internal static Task ExecuteAsync(bool unknownTarget, CancellationToken token)
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            var initialPosition = source.Store.Position;
            await Assert.That(initialPosition).IsEqualTo(D2ResourceInventoryImportData.Single);
            await Assert.That(target.Store.Position).IsEqualTo(D2ResourceInventoryImportData.Single);
            if (unknownTarget)
            {
                D2ResourceInventoryImportSeed.AdditionalAuthentication(target);
            }
            var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
            var lifetime = await D2ResourceInventoryImportSeed.TargetAsync(target, corpus, token);
            var installed = await BorrowAsync(source, target, listeners, corpus, seeded.Receipt, initialPosition, token);
            await D2ResourceInventoryImportCold.ExecuteAsync(source, target, corpus, installed,
                lifetime, seeded, unknownTarget, token);
        });

    private static async Task<D2ResourceInventoryImportResult> BorrowAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, byte[] receipt, long initialPosition, CancellationToken token)
    {
        var controlKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(D2ResourceInventoryImportData.KeyBytes));
        var targetKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(D2ResourceInventoryImportData.KeyBytes));
        var sourceRuntime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus, controlKey, targetKey, control: true);
        var targetRuntime = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus, controlKey, targetKey, control: false);
        D2ResourceInventoryImportResult? installed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceAdmission = Open(source, sourceRuntime);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var targetAdmission = Open(target, targetRuntime);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var caller = ControlledPartitionMovementPrepareRequest.CallerAddress(listeners);
                    var captured = await ControlledPartitionMovementCaptureScenario.ExecuteAsync(source, target,
                        corpus, sourceRuntime, sourceAdmission, caller, receipt, initialPosition, token);
                    var image = new D2ResourceInventoryImportImage(captured.Handle, captured.Pages, captured.Captured,
                        captured.Settlement.Fence ?? throw new InvalidOperationException("The actual native source fence is absent."));
                    await ImageAsync(image);
                    await ControlledPartitionMovementTargetStageFlow.ExecuteAsync(source, target,
                        sourceRuntime, targetRuntime, sourceAdmission, targetAdmission, corpus, image.Captured,
                        image.Fence, image.Handle, image.Pages, caller, image.Handle.ExpiresAt, token);
                    installed = await D2ResourceInventoryImportInstall.ExecuteAsync(new(source, target, corpus,
                        sourceRuntime, targetRuntime, sourceAdmission, targetAdmission, caller), image, token);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return installed ?? throw new InvalidOperationException("The original native image did not complete.");
    }

    private static async Task ImageAsync(D2ResourceInventoryImportImage image)
    {
        await Assert.That(image.Handle.Descriptor.Families.Select(family => family.Family)
            .SequenceEqual(PartitionRecordFamilies.All, StringComparer.Ordinal)).IsTrue();
        await Assert.That(image.Handle.PageCount).IsEqualTo(image.Pages.Length);
        await Assert.That(image.Handle.PageCount).IsEqualTo(image.Handle.Descriptor.Families.Sum(family => family.PageCount));
        var members = image.Handle.Descriptor.Families.Single(family =>
            family.Family == PartitionRecordFamilies.ResourceOwnerInventoryMember);
        await Assert.That(members.RecordCount).IsEqualTo((long)ControlledPartitionMovementResourceAssertions.Expected().Length);
        var proof = image.Handle.Descriptor.Families.Single(family =>
            family.Family == PartitionRecordFamilies.ResourceOwnerInventoryProof);
        await Assert.That(proof.RecordCount).IsEqualTo(D2ResourceInventoryImportData.Single);
    }

    private static PartitionMovementPeerAdmission Open(ControlledPartitionMovementNode owner, ServerRuntimeOptions runtime)
        => new(runtime.Node, owner.Database, runtime.ReplicaConfiguration, runtime.GrainRouting,
            runtime.Membership, runtime.PartitionMovement, owner.Database.EvaluationClock);
}
