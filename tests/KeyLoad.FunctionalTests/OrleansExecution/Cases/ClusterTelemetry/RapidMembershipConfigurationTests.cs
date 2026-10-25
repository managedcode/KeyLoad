using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Orleans.RapidMembership;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class RapidMembershipConfigurationTests
{
    private const string Secret = "membership-authentication-independent-secret";

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(65535, true)]
    [Arguments(65536, false)]
    public async Task PrivateListenerAcceptsOnlyAvailableOrValidTcpPorts(int port, bool valid)
    {
        var options = new OrleansHostingOptions { SharedSecret = Secret, ListenPort = port };
        await Assert.That(options.IsValid()).IsEqualTo(valid);
    }

    [Test]
    [Arguments("", false)]
    [Arguments("                               ", false)]
    [Arguments("1234567890123456789012345678901", false)]
    [Arguments("12345678901234567890123456789012", true)]
    [Arguments("абвгабвгабвгабвг", true)]
    public async Task MembershipAuthenticationRequiresAtLeast32Utf8Bytes(string secret, bool valid)
    {
        var options = new OrleansHostingOptions { SharedSecret = secret };
        await Assert.That(options.IsValid()).IsEqualTo(valid);
    }

    [Test]
    public async Task HostConfigurationReachesPublishedProviderWithoutSeedOrStorageSettings()
    {
        var builder = WebApplication.CreateBuilder([]);
        var section = OrleansHostingOptions.SectionName;
        builder.Configuration[section + ":SharedSecret"] = Secret;
        builder.Configuration[section + ":ListenPort"] = "11112";
        builder.Configuration[section + ":DiscoveryNetworks:0"] = "10.42.0.0/24";
        builder.Configuration[section + ":DiscoveryNetworks:1"] = "192.168.1.10-192.168.1.20";
        OrleansClustering.RegisterHostingOptions(builder);
        await using var services = builder.Services.BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<RapidMembershipOptions>>().Value;
        await Assert.That(options.SharedSecret).IsEqualTo(Secret);
        await Assert.That(options.ListenPort).IsEqualTo(11112);
        await Assert.That(options.DiscoveryNetworks).IsEquivalentTo(new[] { "10.42.0.0/24", "192.168.1.10-192.168.1.20" });
    }
}
