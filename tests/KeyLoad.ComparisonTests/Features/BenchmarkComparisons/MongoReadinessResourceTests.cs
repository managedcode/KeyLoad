using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class MongoReadinessResourceTests
{
    private const string Target = "MongoDB";
    private const string Bootstrap = "isolated-mongo-bootstrap";
    private const string ReadinessMount = "/bootstrap/isolated-mongo-readiness.js";
    private const string ClientMount = "/bootstrap/isolated-mongo.js";
    private const string ReadinessFile = "IsolatedMongoReadiness.js";
    private const string Load = "await load(MongoBootstrap.readinessPath)";
    private const string EntryFile = "IsolatedMongoEntry.sh";
    private const string ClientFile = "IsolatedMongoInitiate.js";
    private const string Scripts = "Features/BenchmarkComparisons";
    private const string TemporaryPrefix = "keyload-mongo-missing-readiness-";
    private const string NodePrefix = "isolated-mongo-";
    private const int GroupCharacters = 32;
    private const char GroupSeparator = '-';

    /// <summary>AC-MR-031-001/003: the actual same-image bootstrap receives the same source read-only.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EveryNativeTopologyMountsReadinessSourceReadOnly(int nodes)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, nodes);
        IsolatedMongoResources.Add(fixture.Context);
        var bootstrap = fixture.Build().Single(resource => resource.Name == Bootstrap);
        var readiness = bootstrap.Annotations.OfType<ContainerMountAnnotation>()
            .Single(mount => mount.Target == ReadinessMount);
        await Assert.That(readiness.IsReadOnly).IsTrue();
        await Assert.That(readiness.Type).IsEqualTo(ContainerMountType.BindMount);
        await Assert.That(Path.GetFileName(readiness.Source)).IsEqualTo(ReadinessFile);
        await Assert.That(File.Exists(readiness.Source)).IsTrue();
        var client = bootstrap.Annotations.OfType<ContainerMountAnnotation>().Single(mount => mount.Target == ClientMount);
        var source = await File.ReadAllTextAsync(client.Source!, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(source.Contains(Load, StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("KeyLoadMongoReadiness.waitReady(hosts, set, deadline, admin)", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>AC-MR-031-003: missing genuine helper bytes fail composition before native resources start.</summary>
    [Test]
    public async Task MissingReadinessSourceRejectsNativeComposition()
    {
        var directory = Directory.CreateTempSubdirectory(TemporaryPrefix).FullName;
        try
        {
            await using var source = new IsolatedResourceTopologyFixture(Target, 1);
            var original = IsolatedMongoBootstrap.FindScripts(source.Context);
            var scripts = Directory.CreateDirectory(Path.Combine(directory, Scripts)).FullName;
            File.Copy(original.Entry, Path.Combine(scripts, EntryFile));
            File.Copy(original.Client, Path.Combine(scripts, ClientFile));
            await using var incomplete = new IsolatedResourceTopologyFixture(Target, 1, directory);
            await Assert.That(() => IsolatedMongoResources.Add(incomplete.Context)).Throws<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>AC-MR-031-004: explicit native identity stays separate from the unchanged selected DNS membership.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NativeNodesHaveUniqueExplicitNamesWithinOnePrivateGroup(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedMongoResources.Add(fixture.Context);
        var nodes = fixture.Build().Where(resource => resource.Name.StartsWith(NodePrefix, StringComparison.Ordinal)
            && resource.Name != Bootstrap).ToArray();
        var names = nodes.Select(node => node.Annotations.OfType<ContainerNameAnnotation>().Single().Name).ToArray();
        await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(count);
        string? group = null;
        foreach (var node in nodes)
        {
            var name = node.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
            await Assert.That(name.StartsWith(node.Name + GroupSeparator, StringComparison.Ordinal)).IsTrue();
            var suffix = name[(node.Name.Length + 1)..];
            await Assert.That(suffix.Length).IsEqualTo(GroupCharacters);
            await Assert.That(suffix.All(char.IsAsciiHexDigitLower)).IsTrue();
            group ??= suffix;
            await Assert.That(suffix).IsEqualTo(group);
        }
    }
}
