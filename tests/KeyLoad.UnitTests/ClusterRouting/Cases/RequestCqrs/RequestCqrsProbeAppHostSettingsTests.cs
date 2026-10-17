using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeAppHostSettingsTests
{
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const string Enabled = "KeyLoadTests:RequestCqrsProbe:Enabled";
    private const string Root = "KeyLoadTests:RequestCqrsProbe:Root";
    private const string Session = "KeyLoadTests:RequestCqrsProbe:SessionId";

    [Test]
    public async Task AbsentAndExplicitlyDisabledSettingsAdmitNoProfile()
    {
        await Assert.That(RequestCqrsProbeProfileSettingsReader.Read(Configuration([]))).IsNull();
        await Assert.That(RequestCqrsProbeProfileSettingsReader.Read(Configuration([(Enabled, "false")]))).IsNull();
    }

    [Test]
    public async Task UnknownNestedMissingAndMalformedSettingsFailClosed()
    {
        var cases = new[]
        {
            Configuration([("KeyLoadTests:RequestCqrsProbe:Unexpected", "x")]),
            Configuration([(Enabled, "true"), ("KeyLoadTests:RequestCqrsProbe:Nested:Value", "x")]),
            Configuration([(Enabled, "perhaps")]),
            Configuration([(Enabled, "true"), (Root, "/private/tmp/root")]),
            Configuration([(Enabled, "true"), (Root, "/private/tmp/root"), (Session, "8D884D084CBD4C508FA4F23D8F61F04A")]),
            Configuration([(Root, "/private/tmp/root")])
        };
        foreach (var configuration in cases)
        {
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfileSettingsReader.Read(configuration));
            await Assert.That(error.Message).IsEqualTo(InvalidConfiguration);
        }
    }

    [Test]
    public async Task DisabledSettingsRejectProfileResidue()
    {
        var configuration = Configuration([(Enabled, "false"), (Root, "/private/tmp/root")]);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsProbeProfileSettingsReader.Read(configuration));
        await Assert.That(error.Message).IsEqualTo(InvalidConfiguration);
    }

    private static IConfiguration Configuration(IEnumerable<(string Key, string? Value)> entries)
    {
        var values = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
