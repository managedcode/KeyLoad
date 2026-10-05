using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsProbeAppHostBuilder
{
    internal const string ValidImage = "ghcr.io/managedcode/keyload:current@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string Session = "8d884d08cb4d4c508fa4f23d8f61f04a";
    internal const string EnabledKey = "KeyLoadTests:RequestCqrsProbe:Enabled";
    internal const string RootKey = "KeyLoadTests:RequestCqrsProbe:Root";
    internal const string SessionKey = "KeyLoadTests:RequestCqrsProbe:SessionId";
    internal const string EphemeralKey = "KeyLoad:Ephemeral";

    internal static IDistributedApplicationBuilder CreateBuilder(
        RequestCqrsProbeAppHostFileFixture fixture, params KeyValuePair<string, string?>[] extra)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [EnabledKey] = "true",
            [RootKey] = fixture.Root,
            [SessionKey] = fixture.SessionId,
            [EphemeralKey] = "true",
            [RuntimeContainerImage.ServerConfiguration] = ValidImage
        };
        foreach (var pair in extra)
        { values[pair.Key] = pair.Value; }
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    internal static IReadOnlyDictionary<string, RuntimeContainerImage> ReadThreeImages(
        IDistributedApplicationBuilder builder)
        => ProtocolCohortImages.Read(builder, ephemeral: true, benchmarkNodeCount: null);
}
