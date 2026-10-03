using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologySelectionTests
{
    private const string KeyLoad = "KeyLoad";
    private const string Redis = "Redis";
    private const string Neo4j = "Neo4j";
    private const string MissingSourcePrefix = "keyload-missing-bootstrap-";

    /// <summary>AC-ISO-003: a mismatched selected target cannot allocate a foreign engine or credentials.</summary>
    [Test]
    [Arguments(KeyLoad, Redis)]
    [Arguments(Redis, Neo4j)]
    [Arguments(Neo4j, KeyLoad)]
    public async Task HelpersRejectForeignTargetBeforeAllocation(string helper, string target)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(target, 1);
        await Assert.That(() => Add(helper, fixture.Context)).Throws<InvalidOperationException>();
        await VerifyUnallocatedAsync(fixture);
    }

    /// <summary>AC-ISO-003/004: invalid native counts never construct an unqualified substitute group.</summary>
    [Test]
    [Arguments(KeyLoad, 0)]
    [Arguments(KeyLoad, 4)]
    [Arguments(Redis, 0)]
    [Arguments(Redis, 4)]
    [Arguments(Neo4j, 0)]
    [Arguments(Neo4j, 4)]
    public async Task HelpersRejectOutOfContractNativeCountsBeforeAllocation(string helper, int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(helper, count);
        await Assert.That(() => Add(helper, fixture.Context)).Throws<InvalidOperationException>();
        await VerifyUnallocatedAsync(fixture);
    }

    /// <summary>AC-ISO-003/005: missing owned Redis bootstrap fails before password or native allocation.</summary>
    [Test]
    public async Task RedisRequiresTheActualSourceControlledBootstrapFile()
    {
        var directory = Directory.CreateTempSubdirectory(MissingSourcePrefix).FullName;
        try
        {
            await using var fixture = new IsolatedResourceTopologyFixture(Redis, 1, directory);
            await Assert.That(() => IsolatedRedisResources.Add(fixture.Context)).Throws<InvalidOperationException>();
            await VerifyUnallocatedAsync(fixture);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task VerifyUnallocatedAsync(IsolatedResourceTopologyFixture fixture)
    {
        var resources = fixture.Build();
        await Assert.That(resources.Length).IsEqualTo(1);
        await Assert.That(resources[0].Name).IsEqualTo(IsolatedResourceTopologyFixture.RunnerName);
        await Assert.That(fixture.Context.Builder.Resources.OfType<ParameterResource>().Any()).IsFalse();
        await Assert.That(Directory.Exists(fixture.Context.Root)).IsFalse();
    }

    private static void Add(string helper, IsolatedResourceContext context)
    {
        switch (helper)
        {
            case KeyLoad:
                IsolatedKeyLoadResources.Add(context);
                break;
            case Redis:
                IsolatedRedisResources.Add(context);
                break;
            case Neo4j:
                IsolatedNeo4jResources.Add(context);
                break;
            default:
                throw new InvalidOperationException();
        }
    }
}
