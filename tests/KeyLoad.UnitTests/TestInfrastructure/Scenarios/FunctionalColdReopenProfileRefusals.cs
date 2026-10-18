using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using P = KeyLoad.UnitTests.Features.TestInfrastructure.FunctionalColdReopenProfileProtocol;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalColdReopenProfileRefusals
{
    internal static async Task RunAsync()
    {
        foreach (var changes in Rejected())
        {
            var root = P.NewRoot();
            var builder = P.Create(root, 3, true, true, changes);
            var original = builder.Resources.Count;
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => KeyLoadResources.AddKeyLoad(builder));
            await Assert.That(builder.Resources.Count).IsEqualTo(original);
            await Assert.That(Directory.Exists(root) || File.Exists(root)).IsFalse();
        }
        await FunctionalColdReopenProfileModel.VerifyAsync(3, true, true);
        await FunctionalFileOwnershipProfileRefusals.RunAsync();
    }

    private static IEnumerable<KeyValuePair<string, string?>[]> Rejected()
    {
        yield return [new(FunctionalColdReopenProfile.Setting, P.False)];
        yield return [new(FunctionalColdReopenProfile.Setting, string.Empty)];
        yield return [new(FunctionalColdReopenProfile.Setting, "invalid")];
        yield return [new(FunctionalColdReopenProfile.Setting + P.Nested, P.True)];
        yield return [new(P.Nodes, null)];
        yield return [new(P.Nodes, "2")];
        yield return [new(P.Nodes + P.Nested, P.True)];
        yield return [new(ContainerFaultCommandProtocol.EnabledSetting, null)];
        yield return [new(ContainerFaultCommandProtocol.EnabledSetting, P.False)];
        yield return [new(FunctionalColdReopenProfile.Setting, null)];
        yield return [new(P.Ephemeral, P.False)];
        yield return [new(P.DataRoot, string.Empty)];
        yield return [new(P.DataRoot, "relative")];
        yield return [new(P.Suite, "unit")];
        yield return [new(P.LoggerControl, P.True)];
        yield return [new(P.Benchmark, P.True)];
    }
}
