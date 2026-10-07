using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipEnvironmentTests
{
    private const string ConfigurationSeparator = ":";
    private const string NodeSection = "KeyLoad";
    private const string MembershipSection = NodeSection + ":MembershipAuthority";
    private const string EnvironmentSeparator = "__";
    private const string AuthoritySection = "KeyLoad__MembershipAuthority__";
    private const string FormerNestedTrustedGroupPrefix = AuthoritySection + "TrustedGroup__";

    [Test]
    public async Task ActualTwoRf3ResourceEnvironmentBindsAndPassesStrictNativeValidation()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(VerifyEmittedResourcesAsync);
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
        MembershipAuthoritySettingsValidator.ValidateSection(
            configuration.GetSection(MembershipSection), node.MembershipAuthority);
        node.Validate();
        await Assert.That(environment.Keys.Any(key => key.StartsWith(FormerNestedTrustedGroupPrefix, StringComparison.Ordinal)))
            .IsFalse();
        var isAuthority = index < TwoRf3ProfileProtocol.MembersPerGroup;
        await Assert.That(node.MembershipAuthority.Mode)
            .IsEqualTo(isAuthority ? MembershipAuthoritySettingsProtocol.Authority : MembershipAuthoritySettingsProtocol.Proxy);
        await VerifyFlatPropertyNamesAsync(environment, isAuthority);
    }

    private static async Task VerifyFlatPropertyNamesAsync(IReadOnlyDictionary<string, string> environment, bool isAuthority)
    {
        var properties = isAuthority
            ? new[]
            {
                nameof(MembershipAuthoritySettings.TrustedGroupPhysicalShardId),
                nameof(MembershipAuthoritySettings.TrustedGroupIncarnation),
                nameof(MembershipAuthoritySettings.TrustedGroupPeerSecret),
                nameof(MembershipAuthoritySettings.TrustedGroupVoterIds),
                nameof(MembershipAuthoritySettings.TrustedGroupSiloEndpoints)
            }
            : new[]
            {
                nameof(MembershipAuthoritySettings.AuthorityPhysicalShardId),
                nameof(MembershipAuthoritySettings.AuthorityIncarnation),
                nameof(MembershipAuthoritySettings.AuthorityPeerSecret),
                nameof(MembershipAuthoritySettings.AuthorityEndpoints)
            };
        foreach (var property in properties)
        {
            var prefix = AuthoritySection + property;
            await Assert.That(environment.Keys.Any(key => key == prefix
                || key.StartsWith(prefix + EnvironmentSeparator, StringComparison.Ordinal))).IsTrue();
        }
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
        var bytes = RandomNumberGenerator.GetBytes(MembershipAuthoritySettingsProtocol.SecretBytes);
        try
        { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
