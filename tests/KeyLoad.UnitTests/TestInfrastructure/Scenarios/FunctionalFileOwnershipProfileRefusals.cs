using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using P = KeyLoad.UnitTests.Features.TestInfrastructure.FunctionalColdReopenProfileProtocol;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalFileOwnershipProfileRefusals
{
    private const int Voters = 3;
    private const string InvalidOption = "invalid";
    private const string SingleVoter = "1";
    private const string InvalidVoters = "2";
    private const string UnitSuite = "unit";

    internal static async Task RunAsync()
    {
        foreach (var changes in Rejected())
        {
            var root = P.NewRoot();
            var values = new List<KeyValuePair<string, string?>>
            {
                new(ExclusiveOwnerContentionProfile.Setting, P.True)
            };
            values.AddRange(changes);
            var builder = P.Create(root, Voters, true, true, [.. values]);
            var original = builder.Resources.Count;
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => KeyLoadResources.AddKeyLoad(builder));
            await Assert.That(builder.Resources.Count).IsEqualTo(original);
            await Assert.That(Directory.Exists(root) || File.Exists(root)).IsFalse();
        }
        await FunctionalFileOwnershipProfileModel.VerifyAsync();
    }

    private static IEnumerable<KeyValuePair<string, string?>[]> Rejected()
    {
        yield return [new(ExclusiveOwnerContentionProfile.Setting, InvalidOption)];
        yield return [new(ExclusiveOwnerContentionProfile.Setting, P.False)];
        yield return [new(ExclusiveOwnerContentionProfile.Setting, string.Empty)];
        yield return [new(ExclusiveOwnerContentionProfile.Setting + P.Nested, P.True)];
        yield return [new(P.Ephemeral, P.False)];
        yield return [new(FunctionalColdReopenProfile.Setting, null)];
        yield return [new(ContainerFaultCommandProtocol.EnabledSetting, null)];
        yield return [new(P.Nodes, null)];
        yield return [new(P.Nodes, SingleVoter)];
        yield return [new(P.Nodes, InvalidVoters)];
        yield return [new(P.Nodes + P.Nested, P.True)];
        yield return [new(P.Benchmark, P.True)];
        yield return [new(P.Suite, UnitSuite)];
        yield return [new(P.LoggerControl, P.True)];
    }
}
