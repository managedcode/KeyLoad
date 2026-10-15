using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.TestInfrastructure;

[NotInParallel]
internal sealed class WholeClassRf3FixtureAdmissionTests
{
    private const string Suite = "--KeyLoadTests:Suite=rf3";
    private const string FilterPrefix = "--KeyLoadTests:Filter=";
    private const string Enabled = "--KeyLoadTests:LocalRf3Image:Enabled=true";
    private const string UnknownFilter = "/*/*/UnknownReleaseCase/*";
    private const string Whitespace = " ";
    private const string Invalid = "The local RF3 image selection is invalid.";

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    public async Task ExactFixtureArgumentsRejectForeignIdentitiesAndRestoreOriginalSelectionWithoutStartingResources(int selection)
    {
        var original = WholeClassRf3AdmissionEnvironment.IdentityKeys.Append(WholeClassRf3AdmissionEnvironment.Arguments)
            .ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var environment = new WholeClassRf3AdmissionEnvironment();
            await ServerFailureObserver.ObserveAsync(() => BodyAsync(selection), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var item in original)
        {
            await Assert.That(Environment.GetEnvironmentVariable(item.Key)).IsEqualTo(item.Value);
        }
    }

    private static async Task BodyAsync(int selection)
    {
        var filter = WholeClassRf3AdmissionFilters.For(selection);
        string[] expected = [Suite, FilterPrefix + filter, Enabled];
        var encoded = JsonSerializer.Serialize(expected);
        Environment.SetEnvironmentVariable(WholeClassRf3AdmissionEnvironment.Arguments, encoded);
        await HealthyAsync(expected, encoded);
        foreach (var key in WholeClassRf3AdmissionEnvironment.IdentityKeys)
        {
            Environment.SetEnvironmentVariable(key, WholeClassRf3AdmissionEnvironment.Foreign);
            try
            {
                await RejectedAsync(encoded);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
            await HealthyAsync(expected, encoded);
        }
        foreach (var changed in new[]
        {
            new[] { Suite, FilterPrefix + UnknownFilter, Enabled },
            new[] { Suite, FilterPrefix + filter + Whitespace, Enabled },
            new[] { Suite, FilterPrefix + filter[..^1] + "UnknownMethod", Enabled },
            new[] { Suite, FilterPrefix + filter.Replace("Tests", "Tests*", StringComparison.Ordinal), Enabled },
            new[] { Suite, FilterPrefix + filter + "|" + UnknownFilter, Enabled },
            new[] { Suite, FilterPrefix + ChangedOrder(filter), Enabled },
            new[] { Suite, FilterPrefix + filter, Enabled, WholeClassRf3AdmissionEnvironment.Foreign },
            new[] { Enabled, FilterPrefix + filter, Suite },
        })
        {
            var damaged = JsonSerializer.Serialize(changed);
            Environment.SetEnvironmentVariable(WholeClassRf3AdmissionEnvironment.Arguments, damaged);
            await RejectedAsync(damaged);
            Environment.SetEnvironmentVariable(WholeClassRf3AdmissionEnvironment.Arguments, encoded);
            await HealthyAsync(expected, encoded);
        }
    }

    private static string ChangedOrder(string filter)
    {
        const string CombinedPrefix = "/*/*/(";
        const string CombinedSuffix = ")/*";
        if (!filter.StartsWith(CombinedPrefix, StringComparison.Ordinal))
        {
            return UnknownFilter;
        }
        var classes = filter[CombinedPrefix.Length..^CombinedSuffix.Length].Split('|');
        return CombinedPrefix + string.Join('|', classes.Reverse()) + CombinedSuffix;
    }

    private static async Task HealthyAsync(string[] expected, string encoded)
    {
        var actual = LocalRf3ImageSelection.ReadNativeArgumentsIfSelected();
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(Environment.GetEnvironmentVariable(WholeClassRf3AdmissionEnvironment.Arguments)).IsEqualTo(encoded);
        foreach (var key in WholeClassRf3AdmissionEnvironment.IdentityKeys)
        {
            await Assert.That(Environment.GetEnvironmentVariable(key)).IsNull();
        }
    }

    private static async Task RejectedAsync(string encoded)
    {
        var original = WholeClassRf3AdmissionEnvironment.IdentityKeys.Append(WholeClassRf3AdmissionEnvironment.Arguments)
            .ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        InvalidOperationException? denied = null;
        try
        {
            _ = LocalRf3ImageSelection.ReadNativeArgumentsIfSelected();
        }
        catch (InvalidOperationException failure)
        {
            denied = failure;
        }
        await Assert.That(denied).IsNotNull();
        await Assert.That(denied!.Message).IsEqualTo(Invalid);
        await Assert.That(Environment.GetEnvironmentVariable(WholeClassRf3AdmissionEnvironment.Arguments)).IsEqualTo(encoded);
        foreach (var item in original)
        {
            await Assert.That(Environment.GetEnvironmentVariable(item.Key)).IsEqualTo(item.Value);
        }
    }
}
