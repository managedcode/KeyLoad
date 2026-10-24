using KeyLoad.AppHost.Hosting;
using KeyLoad.AppHost.StorageRecovery;
using KeyLoad.AppHost.TestInfrastructure;
using P = KeyLoad.UnitTests.TestInfrastructure.FunctionalColdReopenProfileProtocol;

namespace KeyLoad.UnitTests.TestInfrastructure;

internal static class FunctionalFileOwnershipProfileRefusals
{
    private const int Silos = 3;
    private const string InvalidOption = "invalid";
    private const string SingleSilo = "1";
    private const string InvalidSilos = "2";

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
            var builder = P.Create(root, Silos, true, true, [.. values]);
            var original = builder.Resources.Count;
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => AppHostProgram.AddKeyLoad(builder));
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
        yield return [new(ProcessFaultCommandProtocol.EnabledSetting, null)];
        yield return [new(P.Nodes, null)];
        yield return [new(P.Nodes, SingleSilo)];
        yield return [new(P.Nodes, InvalidSilos)];
        yield return [new(P.Nodes + P.Nested, P.True)];
        yield return [new(P.Benchmark, P.True)];
        yield return [new(P.LoggerControl, P.True)];
    }
}
