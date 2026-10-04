using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedDocumentResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedDocumentResourceMongoTests
{
    private const string Target = "MongoDB";
    private const string Prefix = "isolated-mongo-";
    private const string Bootstrap = "isolated-mongo-bootstrap";
    private const string Password = "MONGO_INITDB_ROOT_PASSWORD";
    private const string Key = "KEYLOAD_MONGO_REPLICATION_KEY";
    private const string Script = "/bootstrap/isolated-mongo.sh";
    private const string BootstrapScript = "/bootstrap/isolated-mongo.js";
    private const string ReadinessScript = "/bootstrap/isolated-mongo-readiness.js";
    private const string TopLevelAwait = "await bootstrap();";
    private const string SetupDeadline = "const deadline = Date.now() + 120000;";
    private const string BootstrapCompletion = """
        bootstrap().then(
            () => quit(0),
            error => {
                // Native failures are retained as a fixed classification without credential-bearing details.
                if (lastDiagnostic === null || lastDiagnostic.predicate === MongoDiagnosticPredicate.none || lastDiagnostic.predicate === MongoDiagnosticPredicate.awaiting) rememberException(error);
                print('MongoNativeBootstrapFailed');
                printDiagnostic();
                quit(1);
            }
        );
        """;

    /// <summary>AC-ISO-002/003/005/006: exact native nodes and a bounded authenticated bootstrap completion barrier.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NativeGroupUsesSameImageSecretKeyAndBoundedBootstrapBarrier(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedMongoResources.Add(fixture.Context);
        var resources = fixture.Build();
        var nodes = resources.Where(node => node.Name.StartsWith(Prefix, StringComparison.Ordinal) && node.Name != Bootstrap).ToArray();
        await IsolatedDocumentResourceAssertions.VerifyNodesAsync(nodes, count, Prefix, BenchmarkResources.MongoDigest);
        await Assert.That(resources.Length).IsEqualTo(count + 2);
        var password = await IsolatedDocumentResourceAssertions.SecretAsync(nodes[0], Password);
        foreach (var node in nodes)
        {
            await IsolatedDocumentResourceAssertions.VerifyDataAsync(node, T.MongoData, fixture.Context.Root);
            await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(node, Password)).IsSameReferenceAs(password);
            await IsolatedDocumentResourceAssertions.VerifyScriptAsync(node, Script,
                "chmod 600", "chown mongodb:mongodb", "/usr/local/bin/docker-entrypoint.sh");
            var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
            await Assert.That(configuration.Arguments.Any(argument => argument.Value == T.ReplicaOption)).IsEqualTo(count > 1);
            await Assert.That(node.Annotations.OfType<WaitAnnotation>()).IsEmpty();
        }
        await IsolatedDocumentResourceAssertions.VerifyEndpointsAsync(fixture.Context.Runner.Resource, nodes, T.Tcp);
        await VerifyBootstrapAsync(fixture, resources.Single(node => node.Name == Bootstrap), nodes, password);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(environment[IsolatedDocumentResourceAssertions.Image]).IsEqualTo(
            "docker.io/library/mongo:8.3.9@" + BenchmarkResources.MongoDigest);
        await Assert.That(environment[IsolatedDocumentResourceAssertions.Connection].Contains("replicaSet=benchmark", StringComparison.Ordinal)).IsEqualTo(count > 1);
        if (count > 1)
        {
            var key = await IsolatedDocumentResourceAssertions.SecretAsync(nodes[0], Key);
            foreach (var node in nodes)
            {
                await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(node, Key)).IsSameReferenceAs(key);
            }
        }
        else
        {
            await Assert.That((await IsolatedResourceTopologyFixture.EnvironmentAsync(nodes[0])).ContainsKey(Key)).IsFalse();
        }
    }

    private static async Task VerifyBootstrapAsync(IsolatedResourceTopologyFixture fixture, ContainerResource bootstrap,
        ContainerResource[] nodes, ParameterResource password)
    {
        await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(bootstrap, Password)).IsSameReferenceAs(password);
        await Assert.That(bootstrap.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256).IsEqualTo(BenchmarkResources.MongoDigest[7..]);
        await Assert.That(bootstrap.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name)).IsEquivalentTo(nodes.Select(node => node.Name));
        await Assert.That(fixture.Context.Runner.Resource.Annotations.OfType<WaitAnnotation>().Any(wait =>
            ReferenceEquals(wait.Resource, bootstrap) && wait.WaitType == WaitType.WaitForCompletion)).IsTrue();
        await IsolatedDocumentResourceAssertions.VerifyScriptAsync(bootstrap, BootstrapScript,
            "process.env", "replSetInitiate", "deadline", "auth", "quit(1)", "hosts.length > MongoBootstrap.one",
            "await load(MongoBootstrap.readinessPath)", "KeyLoadMongoReadiness.waitReady(hosts, set, deadline, admin)");
        await IsolatedDocumentResourceAssertions.VerifyScriptAsync(bootstrap, ReadinessScript,
            "replSetGetStatus", "replSetGetConfig", "isWritablePrimary", "primary === hosts[MongoReadiness.zero]",
            "mongoReadinessSameRound(previous, current)", "pollMs: 500");
        await VerifyCompletionAsync(bootstrap);
        var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(bootstrap);
        await Assert.That(configuration.Arguments.Select(argument => argument.Value).SequenceEqual(
            new[] { T.Shell, T.Quiet, T.NoDatabase, BootstrapScript }, StringComparer.Ordinal)).IsTrue();
    }

    private static async Task VerifyCompletionAsync(ContainerResource bootstrap)
    {
        var mount = bootstrap.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == BootstrapScript);
        var source = await File.ReadAllTextAsync(mount.Source!, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(source.TrimEnd().EndsWith(BootstrapCompletion, StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains(TopLevelAwait, StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains(SetupDeadline, StringComparison.Ordinal)).IsTrue();
    }
}
