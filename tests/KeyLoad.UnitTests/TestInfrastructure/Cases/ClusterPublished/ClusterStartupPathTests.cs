using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class ClusterStartupPathTests
{
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string AdminSetting = "Parameters:admin-key";
    private const string SigningSetting = "Parameters:signing-key";
    private const string IncarnationSetting = "Parameters:incarnation";
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(5)]
    public async Task LinkedParentBuildsRequestedProjectNodesWithoutCredentialFiles(int nodes)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "keyload-startup-path-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Combine(directory, "physical");
        var linked = Path.Combine(directory, "linked");
        Directory.CreateDirectory(physical);
        Directory.CreateSymbolicLink(linked, physical);
        try
        {
            var root = Path.Combine(linked, "cluster");
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
            {
                DisableDashboard = true,
                Args = ["--KeyLoad:Ephemeral=true", "--KeyLoad:DataRoot=" + root,
                    "--KeyLoad:SiloCount=" + nodes, "--KeyLoad:ReplicaCount=1"]
            });
            AppHostProgram.AddKeyLoad(builder);
            await using var application = builder.Build();
            var identity = application.Services.GetRequiredService<ClusterIdentity>();
            var model = application.Services.GetRequiredService<DistributedApplicationModel>();
            await Assert.That(model.Resources.OfType<ProjectResource>().Count()).IsEqualTo(nodes);
            await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();
            await Assert.That(Directory.GetFiles(physical, "*", SearchOption.AllDirectories)).IsEmpty();
            foreach (var node in model.Resources.OfType<ProjectResource>())
            {
                await Assert.That(Directory.Exists(Path.Combine(root, node.Name))).IsTrue();
                await Assert.That(node.GetProjectMetadata().ProjectPath).EndsWith("KeyLoad.csproj");
                await AssertPhysicalPathAsync(builder, node);
            }
            await Assert.That(identity.Incarnation).IsNotEqualTo(Guid.Empty);
            await Assert.That(Convert.FromBase64String(identity.SigningKey).Length).IsEqualTo(32);
            await Assert.That(identity.AdminKey).StartsWith("root.");
        }
        finally
        {
            Directory.Delete(linked);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task SeparateEphemeralApplicationsKeepIndependentCredentials()
    {
        var first = CreateBuilder();
        var second = CreateBuilder();
        var firstConfiguredAdmin = first.Configuration[AdminSetting];
        var secondConfiguredAdmin = second.Configuration[AdminSetting];
        var firstIdentity = AppHostOptionsRegistration.CreateIdentity(first, ephemeral: true);
        var secondIdentity = AppHostOptionsRegistration.CreateIdentity(second, ephemeral: true);
        await Assert.That(firstIdentity.Incarnation == secondIdentity.Incarnation).IsFalse();
        await Assert.That(firstIdentity.SigningKey == secondIdentity.SigningKey).IsFalse();
        await Assert.That(firstIdentity.AdminKey == secondIdentity.AdminKey).IsFalse();
        await Assert.That(first.Configuration[AdminSetting]).IsEqualTo(firstConfiguredAdmin);
        await Assert.That(second.Configuration[AdminSetting]).IsEqualTo(secondConfiguredAdmin);
    }

    [Test]
    public async Task ConfiguredDevelopmentIdentityRemainsStableWithoutADataProfile()
    {
        var builder = CreateBuilder();
        var original = AppHostOptionsRegistration.CreateIdentity(builder, ephemeral: true);
        builder.Configuration[IncarnationSetting] = original.Incarnation.ToString("D");
        builder.Configuration[SigningSetting] = original.SigningKey;
        builder.Configuration[AdminSetting] = original.AdminKey;
        var reopened = AppHostOptionsRegistration.CreateIdentity(builder, ephemeral: false);
        await Assert.That(reopened.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(reopened.SigningKey == original.SigningKey).IsTrue();
        await Assert.That(reopened.AdminKey == original.AdminKey).IsTrue();
    }

    private static async Task AssertPhysicalPathAsync(IDistributedApplicationBuilder builder, ProjectResource node)
    {
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, node, environment,
            TestContext.Current!.Execution.CancellationToken);
        foreach (var callback in node.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        var path = (string)environment[DataEnvironment];
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            await Assert.That((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0).IsTrue();
        }
    }

    private static IDistributedApplicationBuilder CreateBuilder()
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
}
