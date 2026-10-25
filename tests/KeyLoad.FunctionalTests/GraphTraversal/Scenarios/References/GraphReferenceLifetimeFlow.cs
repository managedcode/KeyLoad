using KeyLoad.Core.GraphTraversal.Serialization;
using KeyLoad.FunctionalTests.ResourceExecution;
using static KeyLoad.FunctionalTests.GraphTraversal.GraphReferenceLifetimeProtocol;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal static class GraphReferenceLifetimeFlow
{
    internal static async Task RunAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        await GraphReferenceLifetimeCalls.SeedAsync(profile, token);
        var anchor = GraphReferenceLifetimeCalls.Reference(profile, AnchorCollection, AnchorId);
        var originalTarget = GraphReferenceLifetimeCalls.Reference(profile,
            DropRuntimeProtocol.Table, DropRuntimeProtocol.Row);
        var original = GraphReferenceLifetimeCalls.Command(OriginalGraph, anchor, originalTarget);
        var originalReceipt = await GraphReferenceLifetimeCalls.CommitAsync(profile, original, token);
        await GraphReferenceLifetimeReceipt.AssertAsync(profile, original, originalReceipt, OriginalGraph, token);
        await GraphReferenceLifetimeReads.HealthyAsync(profile, OriginalGraph, anchor, originalTarget, token);
        await GraphReferenceLifetimeTupleIntegrity.VerifyAsync(profile, OriginalGraph, anchor, originalTarget, token);
        var drop = DropPublicSubmission.Start(profile, new(DropRuntimeProtocol.Tenant,
            DropRuntimeProtocol.Database, DropRuntimeProtocol.Table), Guid.NewGuid(), token);
        _ = DropRuntimeAssertions.Value<DropTableResult>(await drop.Original);
        await DropRuntimeSeed.CreateAsync(profile, token);
        var fresh = GraphReferenceLifetimeCalls.Reference(profile,
            DropRuntimeProtocol.Table, DropRuntimeProtocol.Row);
        await Assert.That(fresh.Lifetime).IsNotNull();
        await Assert.That(fresh.Lifetime).IsNotEqualTo(originalTarget.Lifetime);
        await GraphReferenceLifetimeRefusals.RunAsync(profile, anchor, originalTarget, fresh, token);
        var healthy = GraphReferenceLifetimeCalls.Command(FreshGraph, anchor, fresh);
        var receipt = await GraphReferenceLifetimeCalls.CommitAsync(profile, healthy, token);
        await GraphReferenceLifetimeReceipt.AssertAsync(profile, healthy, receipt, FreshGraph, token);
        var input = GraphCrossPartitionFingerprintInput.Create(DropRuntimeProtocol.Partition, FreshGraph,
            EdgeId, fresh, Revision, false, new(EdgeId, anchor, fresh, Label, Attributes, Revision));
        await DropRuntimeAssertions.SameAsync(fresh, input.Destination);
        await DropRuntimeAssertions.SameAsync(anchor, input.Edge.From);
        await DropRuntimeAssertions.SameAsync(fresh, input.Edge.To);
        for (var cold = FirstCold; cold < ColdCuts; cold++)
        {
            await profile.RestartAsync(token);
            await DropRuntimeAssertions.SameAsync(receipt,
                await GraphReferenceLifetimeCalls.CommitAsync(profile, healthy, token));
            await GraphReferenceLifetimeReads.HealthyAsync(profile, FreshGraph, anchor, fresh, token);
            await GraphReferenceLifetimeTupleIntegrity.VerifyAsync(profile, FreshGraph, anchor, fresh, token);
            await GraphReferenceLifetimeRefusals.RunAsync(profile, anchor, originalTarget, fresh, token);
        }
    }
}
