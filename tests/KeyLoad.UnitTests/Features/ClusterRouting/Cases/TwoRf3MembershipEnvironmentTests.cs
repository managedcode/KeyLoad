using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipEnvironmentTests
{
    private const string ConfigurationSeparator = ":";
    private const string RetiredAuthorityModeKey = "KeyLoad:MembershipAuthority:Mode";
    private const string NodeSection = "KeyLoad";
    private const string EnvironmentSeparator = "__";
    private const string AuthoritySection = "KeyLoad__PhysicalGroupTrust__";
    private const string FormerNestedTrustedGroupPrefix = AuthoritySection + "TrustedGroup__";

    [Test]
    public async Task ActualTwoRf3ResourceEnvironmentBindsAndPassesStrictNativeValidation()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(VerifyEmittedResourcesAsync);
    }

    [Test]
    public async Task RemoteReadAuthorityDependenciesExcludeLateDataAdmission()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var builder = CreateBuilder();
            var resources = TwoRf3ClusterResources.Add(builder, CreateProfile(), fixture.DataRoot);
            TwoRf3RemoteReadResources.Configure(Options.Create(new AppHostControlOptions
            { RemoteDocumentReads = true }), resources, registerOwners: true);
            for (var index = 0; index < resources.Length; index++)
            {
                var checks = resources[index].Resource.Annotations.OfType<HealthCheckAnnotation>()
                    .Select(annotation => annotation.Key).ToArray();
                var name = resources[index].Resource.Name;
                await Assert.That(checks.Contains(name + "_http_/health/ready_200_check", StringComparer.Ordinal))
                    .IsFalse();
                await Assert.That(checks.Contains(name + "_http_/health/membership-authority_200_check", StringComparer.Ordinal))
                    .IsFalse();
                await Assert.That(checks.Contains(name + "_http_/health/membership-ready_200_check", StringComparer.Ordinal))
                    .IsTrue();
            }
        });
    }

    private static async Task VerifyEmittedResourcesAsync(RequestCqrsProbeAppHostFileFixture fixture)
    {
        var builder = CreateBuilder();
        var resources = TwoRf3ClusterResources.Add(builder, CreateProfile(), fixture.DataRoot);
        await Assert.That(resources.Length).IsEqualTo(TwoRf3ProfileProtocol.TotalNodes);
        for (var index = 0; index < resources.Length; index++)
        {
            var configuration = await ExecutionConfigurationBuilder.Create(resources[index].Resource)
                .WithEnvironmentVariablesConfig().BuildAsync(
                    new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
                    NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
            if (configuration.Exception is not null)
            { throw configuration.Exception; }
            await VerifyBoundResourceAsync(configuration.EnvironmentVariables
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), index);
        }
    }

    private static async Task VerifyBoundResourceAsync(IReadOnlyDictionary<string, string> environment, int index)
    {
        var values = environment.Select(pair => new KeyValuePair<string, string?>(
            pair.Key.Replace(EnvironmentSeparator, ConfigurationSeparator, StringComparison.Ordinal), pair.Value));
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        var node = configuration.GetSection(NodeSection).Get<NodeOptions>()
            ?? throw new InvalidOperationException("The emitted membership node configuration did not bind.");
        PhysicalGroupTrustValidator.ValidateConfiguration(configuration.GetSection(NodeSection), node.PhysicalGroupTrust);
        node.Validate();
        await Assert.That(environment.Keys.Any(key => key.StartsWith(FormerNestedTrustedGroupPrefix, StringComparison.Ordinal)))
            .IsFalse();
        var isAuthority = index < TwoRf3ProfileProtocol.MembersPerGroup;
        await Assert.That(node.PhysicalGroupTrust.Role)
            .IsEqualTo(isAuthority ? PhysicalGroupTrustRole.Control : PhysicalGroupTrustRole.Destination);
        await VerifyFlatPropertyNamesAsync(environment);
        await VerifyRefusedAndRepairedAsync(node);
        await VerifyRetiredSectionRefusalAsync(configuration, node);
    }

    private static async Task VerifyFlatPropertyNamesAsync(IReadOnlyDictionary<string, string> environment)
    {
        string[] properties =
        [
            nameof(PhysicalGroupTrustOptions.ForeignPhysicalShardId),
            nameof(PhysicalGroupTrustOptions.ForeignIncarnation),
            nameof(PhysicalGroupTrustOptions.ForeignPeerSecret),
            nameof(PhysicalGroupTrustOptions.ForeignVoterIds)
        ];
        foreach (var property in properties)
        {
            var prefix = AuthoritySection + property;
            await Assert.That(environment.Keys.Any(key => key == prefix
                || key.StartsWith(prefix + EnvironmentSeparator, StringComparison.Ordinal))).IsTrue();
        }
        await Assert.That(environment.Keys.Any(key => key.StartsWith("KeyLoad__MembershipAuthority__", StringComparison.Ordinal)))
            .IsFalse();
    }

    private static async Task VerifyRefusedAndRepairedAsync(NodeOptions node)
    {
        var original = node.PhysicalGroupTrust;
        PhysicalGroupTrustOptions[] denied =
        [
            original with { ForeignPhysicalShardId = node.PhysicalShardId },
            original with { ForeignIncarnation = node.Incarnation },
            original with { ForeignPeerSecret = node.PeerSecret },
            original with { ForeignVoterIds = node.Peers.ToArray() }
        ];
        foreach (var candidate in denied)
        {
            try
            {
                PhysicalGroupTrustValidator.Validate(candidate, node);
            }
            catch (InvalidOperationException error)
            {
                await Assert.That(error.Message).IsEqualTo(PhysicalGroupTrustProtocol.Invalid);
                PhysicalGroupTrustValidator.Validate(original, node);
                node.Validate();
                continue;
            }
            throw new InvalidOperationException("Foreign RF3 trust unexpectedly admitted the local owner or credential.");
        }
    }

    private static async Task VerifyRetiredSectionRefusalAsync(ConfigurationManager original, NodeOptions node)
    {
        using var denied = new ConfigurationManager();
        denied.AddInMemoryCollection(original.AsEnumerable()
            .Append(new KeyValuePair<string, string?>(RetiredAuthorityModeKey, "authority")));
        try
        {
            PhysicalGroupTrustValidator.ValidateConfiguration(denied.GetSection(NodeSection), node.PhysicalGroupTrust);
        }
        catch (InvalidOperationException error)
        {
            await Assert.That(error.Message).IsEqualTo(PhysicalGroupTrustProtocol.Invalid);
            PhysicalGroupTrustValidator.ValidateConfiguration(original.GetSection(NodeSection), node.PhysicalGroupTrust);
            node.Validate();
            return;
        }
        throw new InvalidOperationException("Retired membership-provider settings unexpectedly admitted a native node.");
    }

    private static IDistributedApplicationBuilder CreateBuilder()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RequestCqrsProbeAppHostBuilder.EphemeralKey] = "true",
            [RuntimeContainerImage.ServerConfiguration] = RequestCqrsProbeAppHostBuilder.ValidImage
        });
        return builder;
    }

    private static LocalProfile CreateProfile()
        => new(ClusterProfileStore.CurrentVersion, Guid.NewGuid(), Guid.NewGuid(), CreateSecret(), CreateSecret(),
            "root." + Guid.NewGuid().ToString("N"));

    private static string CreateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(PhysicalGroupTrustProtocol.SecretBytes);
        try
        { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
