using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedDocumentResourceRejectionTests
{
    private const string Postgres = "PostgreSQL + pgvector";
    private const string Mongo = "MongoDB";
    private const string OpenSearch = "OpenSearch";
    private const string Kurrent = "KurrentDB";

    /// <summary>AC-ISO-002/006: reject incompatible selection before any native node, secret or directory allocation.</summary>
    [Test]
    [Arguments(Postgres)]
    [Arguments(Mongo)]
    [Arguments(OpenSearch)]
    [Arguments(Kurrent)]
    public async Task InvalidCountAndForeignTargetAllocateNothing(string target)
    {
        await using var invalid = new IsolatedResourceTopologyFixture(target, 4);
        Assert.ThrowsExactly<InvalidOperationException>(() => Add(target, invalid.Context));
        await Assert.That(invalid.Context.Builder.Resources.Count).IsEqualTo(1);
        await Assert.That(Directory.Exists(invalid.Context.Root)).IsFalse();
        await using var foreign = new IsolatedResourceTopologyFixture("Redis", 1);
        Assert.ThrowsExactly<InvalidOperationException>(() => Add(target, foreign.Context));
        await Assert.That(foreign.Context.Builder.Resources.Count).IsEqualTo(1);
        await Assert.That(Directory.Exists(foreign.Context.Root)).IsFalse();
    }

    /// <summary>AC-ISO-003/006: independent cells never share a native data volume.</summary>
    [Test]
    [Arguments(OpenSearch, "/usr/share/opensearch/data")]
    [Arguments(Kurrent, "/var/lib/kurrentdb")]
    public async Task SeparateCellsHaveDisjointDataVolumeNames(string target, string data)
    {
        await using var first = new IsolatedResourceTopologyFixture(target, 3);
        await using var second = new IsolatedResourceTopologyFixture(target, 3);
        Add(target, first.Context);
        Add(target, second.Context);
        var original = DataVolumes(first.Build(), data);
        var following = DataVolumes(second.Build(), data);
        await Assert.That(original.Length).IsEqualTo(3);
        await Assert.That(original.Intersect(following, StringComparer.Ordinal)).IsEmpty();
    }

    private static string[] DataVolumes(ContainerResource[] resources, string target)
        => resources.SelectMany(node => node.Annotations.OfType<ContainerMountAnnotation>())
            .Where(mount => mount.Target == target).Select(mount => mount.Source!).ToArray();

    private static void Add(string target, IsolatedResourceContext context)
    {
        switch (target)
        {
            case Postgres:
                IsolatedPostgresResources.Add(context);
                break;
            case Mongo:
                IsolatedMongoResources.Add(context);
                break;
            case OpenSearch:
                IsolatedOpenSearchResources.Add(context);
                break;
            case Kurrent:
                IsolatedKurrentResources.Add(context);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
