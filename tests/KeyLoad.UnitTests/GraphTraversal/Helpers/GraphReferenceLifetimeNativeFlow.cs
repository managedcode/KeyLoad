using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.GraphTraversal.GraphReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphReferenceLifetimeNativeFlow
{
    internal static async Task RunAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await GraphReferenceLifetimeNativeCalls.SeedAsync(profile, token);
        var anchor = GraphReferenceLifetimeNativeCalls.Reference(profile, AnchorCollection, AnchorId);
        var originalTarget = GraphReferenceLifetimeNativeCalls.Reference(profile,
            NativeDropRuntimeProtocol.Table, NativeDropRuntimeProtocol.Row);
        var original = GraphReferenceLifetimeNativeCalls.Command(OriginalGraph, anchor, originalTarget);
        var originalReceipt = await GraphReferenceLifetimeNativeCalls.CommitAsync(profile, original, token);
        await GraphReferenceLifetimeNativeReceipt.AssertAsync(profile, original, originalReceipt, OriginalGraph, token);
        await GraphReferenceLifetimeNativeReads.HealthyAsync(profile, OriginalGraph, anchor, originalTarget, token);
        await GraphReferenceLifetimeNativeTupleIntegrity.VerifyAsync(profile, OriginalGraph, anchor, originalTarget, token);
        var drop = NativeDropPublicSubmission.Start(profile, new(NativeDropRuntimeProtocol.Tenant,
            NativeDropRuntimeProtocol.Database, NativeDropRuntimeProtocol.Table), Guid.NewGuid(), token);
        _ = NativeDropRuntimeAssertions.Value<DropTableResult>(await drop.Original);
        await NativeDropRuntimeSeed.CreateAsync(profile, token);
        var fresh = GraphReferenceLifetimeNativeCalls.Reference(profile,
            NativeDropRuntimeProtocol.Table, NativeDropRuntimeProtocol.Row);
        await Assert.That(fresh.Lifetime).IsNotNull();
        await Assert.That(fresh.Lifetime).IsNotEqualTo(originalTarget.Lifetime);
        await GraphReferenceLifetimeNativeRefusals.RunAsync(profile, anchor, originalTarget, fresh, token);
        var healthy = GraphReferenceLifetimeNativeCalls.Command(FreshGraph, anchor, fresh);
        var receipt = await GraphReferenceLifetimeNativeCalls.CommitAsync(profile, healthy, token);
        await GraphReferenceLifetimeNativeReceipt.AssertAsync(profile, healthy, receipt, FreshGraph, token);
        var input = GraphCrossPartitionFingerprintInput.Create(NativeDropRuntimeProtocol.Partition, FreshGraph,
            EdgeId, fresh, Revision, false, new(EdgeId, anchor, fresh, Label, Attributes, Revision));
        await NativeDropRuntimeAssertions.SameAsync(fresh, input.Destination);
        await NativeDropRuntimeAssertions.SameAsync(anchor, input.Edge.From);
        await NativeDropRuntimeAssertions.SameAsync(fresh, input.Edge.To);
        for (var cold = FirstCold; cold < ColdCuts; cold++)
        {
            await profile.RestartAsync(token);
            await NativeDropRuntimeAssertions.SameAsync(receipt,
                await GraphReferenceLifetimeNativeCalls.CommitAsync(profile, healthy, token));
            await GraphReferenceLifetimeNativeReads.HealthyAsync(profile, FreshGraph, anchor, fresh, token);
            await GraphReferenceLifetimeNativeTupleIntegrity.VerifyAsync(profile, FreshGraph, anchor, fresh, token);
            await GraphReferenceLifetimeNativeRefusals.RunAsync(profile, anchor, originalTarget, fresh, token);
        }
    }
}
