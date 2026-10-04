using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyNeo4jTests
{
    private const string Target = "Neo4j";
    private const string User = "neo4j";
    private const string PasswordName = "neo4j-password";
    private const string NativePrefix = IsolatedResourceTopologyFixture.NativePrefix;
    private const string AuthenticationEnvironment = "NEO4J_AUTH";
    private const string InitialHeapEnvironment = "NEO4J_server_memory_heap_initial__size";
    private const string MaximumHeapEnvironment = "NEO4J_server_memory_heap_max__size";
    private const string PageCacheEnvironment = "NEO4J_server_memory_pagecache_size";

    /// <summary>AC-ISO-003/005: Community node uses native basic auth, independent data and immutable image.</summary>
    [Test]
    public async Task SelectedCommunityNeo4jAllocatesOnlyOneNativeNodeAndRunner()
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, 1);
        IsolatedNeo4jResources.Add(fixture.Context);
        var resources = fixture.Build();
        await Assert.That(resources.Length).IsEqualTo(2);
        var neo4j = resources.Single(item => item.Name == User);
        var runner = resources.Single(item => item.Name == IsolatedResourceTopologyFixture.RunnerName);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, [neo4j]);
        await IsolatedResourceTopologyFixture.VerifyPrivateDataAsync(neo4j, fixture.Context.Root);
        await IsolatedResourceTopologyFixture.VerifyUserAsync(neo4j);
        var native = await IsolatedResourceTopologyFixture.EnvironmentAsync(neo4j);
        var bindings = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(native[AuthenticationEnvironment]).IsEqualTo(User + "/{" + PasswordName + ".value}");
        await Assert.That(native[InitialHeapEnvironment]).IsEqualTo("256m");
        await Assert.That(native[MaximumHeapEnvironment]).IsEqualTo("512m");
        await Assert.That(native[PageCacheEnvironment]).IsEqualTo("256m");
        await Assert.That(bindings[NativePrefix + "User"]).IsEqualTo(User);
        await Assert.That(bindings[NativePrefix + "Password"]).IsEqualTo("{" + PasswordName + ".value}");
        await Assert.That(bindings[NativePrefix + "Endpoints__0"]).IsEqualTo("{neo4j.bindings.http.url}");
        await Assert.That(bindings.Keys.Count(key => key.StartsWith(NativePrefix + "Endpoints__", StringComparison.Ordinal)))
            .IsEqualTo(1);
        await Assert.That(bindings[NativePrefix + "Image"])
            .IsEqualTo("docker.io/library/neo4j:2026.09.0@" + BenchmarkResources.Neo4jDigest);
        var password = fixture.Context.Builder.Resources.OfType<ParameterResource>().Single();
        await Assert.That(password.Secret).IsTrue();
        var value = await password.GetValueAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(value!.Length).IsEqualTo(64);
        await Assert.That(value.All(char.IsAsciiHexDigit)).IsTrue();
    }

    /// <summary>AC-ISO-003/006: unsupported Community replication cannot allocate a false native topology.</summary>
    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task CommunityReplicationRejectsBeforeNativeAllocation(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        await Assert.That(() => IsolatedNeo4jResources.Add(fixture.Context)).Throws<InvalidOperationException>();
        var resources = fixture.Build();
        await Assert.That(resources.Length).IsEqualTo(1);
        await Assert.That(resources[0].Name).IsEqualTo(IsolatedResourceTopologyFixture.RunnerName);
        await Assert.That(fixture.Context.Builder.Resources.OfType<ParameterResource>().Any()).IsFalse();
        await Assert.That(Directory.Exists(fixture.Context.Root)).IsFalse();
    }
}
