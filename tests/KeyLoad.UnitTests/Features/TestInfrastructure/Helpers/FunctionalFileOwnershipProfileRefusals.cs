using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
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
    private const string TwoRf3 = "two-rf3";
    private const string UnitSuite = "unit";
    private const string ProbeEnabled = ":Enabled";
    private const string ProbeRoot = ":Root";
    private const string ProbeSession = ":SessionId";
    private const string SessionFormat = "N";

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
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => KeyLoadAppHostApplication.AddKeyLoad(builder));
            await Assert.That(builder.Resources.Count).IsEqualTo(original);
            await Assert.That(Directory.Exists(root) || File.Exists(root)).IsFalse();
            foreach (var probe in changes.Where(value => value.Key == RequestCqrsProbeProfile.Section + ProbeRoot))
            {
                await Assert.That(Directory.Exists(probe.Value) || File.Exists(probe.Value)).IsFalse();
            }
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
        yield return [new(P.TwoRf3, TwoRf3)];
        yield return [new(P.Suite, UnitSuite)];
        yield return [new(P.LoggerControl, P.True)];
        yield return [new(RequestCqrsProbeProfile.Section + ProbeEnabled, P.True),
            new(RequestCqrsProbeProfile.Section + ProbeRoot, P.NewRoot()),
            new(RequestCqrsProbeProfile.Section + ProbeSession, Guid.NewGuid().ToString(SessionFormat))];
    }
}
