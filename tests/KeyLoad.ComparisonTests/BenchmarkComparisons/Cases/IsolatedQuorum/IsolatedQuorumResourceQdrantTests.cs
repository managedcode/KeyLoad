using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedQuorumResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-001/003: real Aspire model selects exact native Qdrant members and private bootstrap wiring.</summary>
internal sealed class IsolatedQuorumResourceQdrantTests
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AcIso003QdrantModelBindsActualMembersStorageAndImmutableImage(int count)
    {
        using var model = new IsolatedQuorumResourceModel();
        model.Add(T.Qdrant, count);
        var nodes = model.BuildNodes();
        await IsolatedQuorumResourceAssertions.VerifyGroupAsync(model, nodes, count, T.QdrantPrefix,
            T.QdrantStorage, T.Http, BenchmarkResources.QdrantDigest);
        var runner = await IsolatedQuorumResourceModel.ReadAsync(model.Runner.Resource);
        await Assert.That(runner.EnvironmentVariables.ToDictionary()[T.Image])
            .IsEqualTo(T.QdrantImage + BenchmarkResources.QdrantDigest);
        var bindings = runner.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
        var key = await IsolatedQuorumResourceAssertions.VerifySecretAsync(bindings[T.ApiKey]);
        foreach (var node in nodes)
        {
            var configuration = await IsolatedQuorumResourceModel.ReadAsync(node);
            var environment = configuration.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
            await Assert.That(ReferenceEquals(environment[T.QdrantKey], key)).IsTrue();
            await Assert.That(node).IsTypeOf<QdrantServerResource>();
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AcIso003QdrantBootstrapPreservesDisabledSingletonAndAdvertisesNativePeerUris(int count)
    {
        using var model = new IsolatedQuorumResourceModel();
        model.Add(T.Qdrant, count);
        var nodes = model.BuildNodes();
        for (var index = 0; index < count; index++)
        {
            var configuration = await IsolatedQuorumResourceModel.ReadAsync(nodes[index]);
            var environment = configuration.EnvironmentVariables.ToDictionary();
            await Assert.That(environment[T.ClusterEnabled]).IsEqualTo(count == 1 ? T.False : T.True);
            var arguments = configuration.Arguments.Select(argument => argument.Value).ToArray();
            var expected = ExpectedArguments(count, index);
            await Assert.That(arguments.SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
            await Assert.That(nodes[index].Entrypoint).IsEqualTo(T.QdrantEntrypoint);
            await Assert.That(nodes[index].Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name))
                .IsEquivalentTo(index == 0 ? Array.Empty<string>() : new[] { nodes[0].Name });
        }
    }

    private static string[] ExpectedArguments(int count, int index)
    {
        if (count == 1)
        {
            return [];
        }
        var uri = T.UriPrefix + T.QdrantPrefix + (index + 1).ToString(CultureInfo.InvariantCulture) + T.PeerPort;
        return index == 0 ? [T.UriFlag, uri] : [T.UriFlag, uri, T.BootstrapFlag, T.PeerOne];
    }
}
