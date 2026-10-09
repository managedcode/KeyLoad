using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedQuorumResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-001/003: native Rabbit peers use common secret auth, exact disc discovery and owned data.</summary>
internal sealed class IsolatedQuorumResourceRabbitTests
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AcIso003RabbitModelBindsActualMembersAndSharedNativeCredentials(int count)
    {
        using var model = new IsolatedQuorumResourceModel();
        model.Add(T.Rabbit, count);
        var nodes = model.BuildNodes();
        await IsolatedQuorumResourceAssertions.VerifyGroupAsync(model, nodes, count, T.RabbitPrefix,
            T.RabbitStorage, T.Management, BenchmarkResources.RabbitDigest);
        var runner = await IsolatedQuorumResourceModel.ReadAsync(model.Runner.Resource);
        var values = runner.EnvironmentVariables.ToDictionary();
        await Assert.That(values[T.Image]).IsEqualTo(T.RabbitImage + BenchmarkResources.RabbitDigest);
        var bindings = runner.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
        var user = await IsolatedQuorumResourceAssertions.VerifySecretAsync(bindings[T.User]);
        var password = await IsolatedQuorumResourceAssertions.VerifySecretAsync(bindings[T.Password]);
        var cookie = await SharedCookieAsync(nodes);
        await Assert.That(ReferenceEquals(user, password) || ReferenceEquals(cookie, password)).IsFalse();
        var first = (RabbitMQServerResource)nodes[0];
        await Assert.That(values[T.Connection]).IsEqualTo(first.ConnectionStringExpression.ValueExpression);
        foreach (var node in nodes)
        {
            var configuration = await IsolatedQuorumResourceModel.ReadAsync(node);
            var environment = configuration.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
            await Assert.That(((RabbitMQServerResource)node).UserNameParameter).IsEqualTo(user);
            await Assert.That(configuration.EnvironmentVariables.ToDictionary()[T.RabbitUser]).IsEqualTo(user.ValueExpression);
            await Assert.That(ReferenceEquals(environment[T.RabbitPassword], password)).IsTrue();
            await Assert.That(ReferenceEquals(environment[T.Cookie], cookie)).IsTrue();
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AcIso003RabbitBootstrapDeclaresExactDiscPeersWithRoutableNodeNamesAndNoSecrets(int count)
    {
        using var model = new IsolatedQuorumResourceModel();
        model.Add(T.Rabbit, count);
        var nodes = model.BuildNodes();
        var expected = ExpectedConfiguration(count);
        for (var index = 0; index < count; index++)
        {
            var configuration = await IsolatedQuorumResourceModel.ReadAsync(nodes[index]);
            var environment = configuration.EnvironmentVariables.ToDictionary();
            await Assert.That(environment[T.NodeName]).IsEqualTo(T.RabbitNamePrefix + nodes[index].Name);
            await Assert.That(environment[T.LongName]).IsEqualTo(T.False);
            await Assert.That(environment[T.RabbitConfigSetting]).IsEqualTo(T.ConfigStem);
            var mount = nodes[index].Annotations.OfType<ContainerMountAnnotation>().Single(mount => mount.Target == T.RabbitConfig);
            await Assert.That(mount.IsReadOnly).IsTrue();
            await Assert.That(mount.Source!.StartsWith(model.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)).IsTrue();
            var actual = await File.ReadAllTextAsync(mount.Source, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(actual).IsEqualTo(expected);
            await Assert.That(nodes[index].Entrypoint).IsNull();
            await Assert.That(configuration.Arguments).IsEmpty();
            await Assert.That(nodes[index].Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name))
                .IsEquivalentTo(index == 0 ? Array.Empty<string>() : new[] { nodes[0].Name });
        }
    }

    private static async Task<ParameterResource> SharedCookieAsync(ContainerResource[] nodes)
    {
        var configuration = await IsolatedQuorumResourceModel.ReadAsync(nodes[0]);
        var environment = configuration.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
        return await IsolatedQuorumResourceAssertions.VerifySecretAsync(environment[T.Cookie]);
    }

    private static string ExpectedConfiguration(int count)
    {
        var lines = new List<string> { T.Discovery, T.Disc };
        for (var index = 1; index <= count; index++)
        {
            var ordinal = index.ToString(CultureInfo.InvariantCulture);
            lines.Add(T.PeerPrefix + ordinal + " = " + T.RabbitNamePrefix + T.RabbitPrefix + ordinal);
        }
        return string.Join('\n', lines) + '\n';
    }
}
