using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class RetiredBenchmarkSelectionTests
{
    private const string ScaleProfile = "KeyLoadTests:ScaleProfile";
    private const string VectorProfile = "KeyLoadTests:VectorProfile";
    private const string OpenLoopRate = "KeyLoadTests:OpenLoopRate";
    private const string OpenLoopCancellationProof = "KeyLoadTests:OpenLoopCancellationProof";
    private const string ComparisonSuite = "comparison";
    private const string ComparisonProject = "KeyLoad.ComparisonTests";
    private const string RunnerName = "tests-comparison";
    private const string SelectedFilter = "/*/*/ActualComparison/*";
    private const string ArgumentPrefix = "--";
    private const string Assignment = "=";
    private const string Whitespace = " ";
    private const string RetiredValue = "retired";
    private const string SourceDirectory = "src";
    private const string AppHostDirectory = "KeyLoad.AppHost";
    private const int NoResources = 0;
    private const int IsolatedRunnerCount = 1;
    private const int ComparisonTimeoutMinutes = 60;

    [Test]
    [Arguments(ScaleProfile)]
    [Arguments(VectorProfile)]
    [Arguments(OpenLoopRate)]
    [Arguments(OpenLoopCancellationProof)]
    public async Task RetiredCliPresenceRejectsEveryValueAndCaseThenOrdinaryComparisonComposes(string key)
    {
        var options = Options.Create(new TestBootstrapOptions());
        foreach (var spelling in new[] { key, key.ToUpperInvariant() })
        {
            foreach (var suffix in new[] { string.Empty, Assignment, Assignment + Whitespace, Assignment + RetiredValue })
            {
                string[] arguments = [ArgumentPrefix + spelling + suffix];
                var original = arguments.ToArray();
                Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(arguments, options));
                await Assert.That(arguments).IsEquivalentTo(original, CollectionOrdering.Matching);
            }
        }
        await RequireHealthyComparisonAsync();
    }

    [Test]
    [Arguments(ScaleProfile)]
    [Arguments(VectorProfile)]
    [Arguments(OpenLoopRate)]
    [Arguments(OpenLoopCancellationProof)]
    public async Task RetiredConfigurationPresenceRejectsBeforeResourcesWithOrWithoutSuiteThenHealthy(string key)
    {
        foreach (var value in new[] { null, string.Empty, Whitespace, RetiredValue })
        {
            await RequireConfigurationRejectedAsync(key.ToUpperInvariant(), value, suite: null);
            await RequireConfigurationRejectedAsync(key, value, ComparisonSuite);
        }
        await RequireHealthyComparisonAsync();
    }

    private static async Task RequireConfigurationRejectedAsync(string key, string? value, string? suite)
    {
        var builder = CreateBuilder();
        builder.Configuration[TestSuiteSettings.SuiteSetting] = suite;
        builder.Configuration[key] = value;
        var original = builder.Configuration.AsEnumerable().ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => AppHostOptionsRegistration.BindTestBootstrap(builder.Configuration));
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(builder.Configuration));
        Assert.ThrowsExactly<InvalidOperationException>(() => KeyLoadAppHostApplication.AddKeyLoad(builder));
        await Assert.That(builder.Resources.Count).IsEqualTo(NoResources);
        await Assert.That(builder.Configuration.AsEnumerable().ToArray())
            .IsEquivalentTo(original, CollectionOrdering.Matching);
    }

    private static async Task RequireHealthyComparisonAsync()
    {
        var builder = CreateBuilder();
        builder.Configuration[TestSuiteSettings.SuiteSetting] = ComparisonSuite;
        builder.Configuration[TestSuiteSettings.FilterSetting] = SelectedFilter;
        var original = builder.Configuration.AsEnumerable().ToArray();
        var selected = TestSuiteSettings.Read(builder.Configuration) ?? throw new InvalidOperationException();
        await Assert.That(selected.Timeout).IsEqualTo(TimeSpan.FromMinutes(ComparisonTimeoutMinutes));
        await Assert.That(selected.MaximumParallelTests).IsEqualTo(IsolatedRunnerCount);
        await Assert.That(selected.Filter).IsEqualTo(SelectedFilter);
        await Assert.That(selected.Project).IsEqualTo(ComparisonProject);
        await Assert.That(builder.Resources.Count).IsEqualTo(NoResources);
        await Assert.That(builder.Configuration.AsEnumerable().ToArray())
            .IsEquivalentTo(original, CollectionOrdering.Matching);
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        await using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        await Assert.That(model.Resources.Count).IsEqualTo(IsolatedRunnerCount);
        await Assert.That(model.Resources.OfType<ExecutableResource>().Single().Name).IsEqualTo(RunnerName);
        await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();
    }

    private static IDistributedApplicationBuilder CreateBuilder()
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(SessionNodeProcess.RepositoryRoot(), SourceDirectory, AppHostDirectory),
            Args = []
        });
}
