using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeAppHostMixedCaptureTests
{
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const string InvalidProtocolConfiguration = "ProtocolCohortTestConfigurationInvalid";
    private const string CaptureKey = "KeyLoadTests:RequestCqrsProbe:DiscoveryCaptureMode";
    private const string CaptureMode = "mixed-interface3-v1";
    private const string CohortEnabled = "KeyLoadTests:ProtocolCohort:Enabled";
    private const string VoterPrefix = "KeyLoadTests:ProtocolCohort:Voters:";
    private const string UnknownVoterKey = VoterPrefix + "unknown";
    private const string Node1 = "node1";
    private const string Node2 = "node2";
    private const string Node3 = "node3";
    private const string CurrentImage = "ghcr.io/managedcode/keyload:current@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PriorImage = "ghcr.io/managedcode/keyload:prior@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public async Task MixedCaptureAdmitsOnlyOneCurrentAndTwoIdenticalPriorImages()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var builder = CreateBuilder(fixture, CurrentImage, PriorImage, PriorImage, CaptureMode);
            var profile = RequestCqrsProbeProfile.Read(builder, fixture.DataRoot, true, null, Images(builder));
            await Assert.That(profile).IsNotNull();
        });
    }

    [Test]
    public async Task MixedCaptureRejectsMissingModeThreeDistinctImagesAndWrongTopology()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var different = CreateBuilder(fixture, PriorImage, CurrentImage, ThirdImage, CaptureMode);
            await AssertRejectedAsync(fixture, different);
            var noMode = CreateBuilder(fixture, CurrentImage, PriorImage, PriorImage, null);
            await AssertRejectedAsync(fixture, noMode);
            var homogeneous = CreateBuilder(fixture, PriorImage, PriorImage, PriorImage, CaptureMode);
            await AssertRejectedAsync(fixture, homogeneous);
            var malformed = CreateBuilder(fixture, CurrentImage, PriorImage, PriorImage, CaptureMode,
                new KeyValuePair<string, string?>(UnknownVoterKey, PriorImage));
            await AssertRejectedAsync(fixture, malformed, InvalidProtocolConfiguration);
        });
    }

    private const string ThirdImage = "ghcr.io/managedcode/keyload:third@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static IDistributedApplicationBuilder CreateBuilder(RequestCqrsProbeAppHostFileFixture fixture,
        string node1, string node2, string node3, string? mode,
        params KeyValuePair<string, string?>[] extra)
    {
        var settings = new List<KeyValuePair<string, string?>>
        {
            new(CohortEnabled, "true"), new(VoterPrefix + Node1, node1),
            new(VoterPrefix + Node2, node2), new(VoterPrefix + Node3, node3)
        };
        if (mode is not null)
        { settings.Add(new(CaptureKey, mode)); }
        settings.AddRange(extra);
        return RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture, [.. settings]);
    }

    private static Dictionary<string, RuntimeContainerImage> Images(IDistributedApplicationBuilder builder)
        => new(StringComparer.Ordinal)
        {
            [Node1] = RuntimeContainerImage.Read(builder, VoterPrefix + Node1),
            [Node2] = RuntimeContainerImage.Read(builder, VoterPrefix + Node2),
            [Node3] = RuntimeContainerImage.Read(builder, VoterPrefix + Node3)
        };

    private static async Task AssertRejectedAsync(RequestCqrsProbeAppHostFileFixture fixture,
        IDistributedApplicationBuilder builder, string expected = InvalidConfiguration)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsProbeProfile.Read(builder, fixture.DataRoot, true, null, Images(builder)));
        await Assert.That(error.Message).IsEqualTo(expected);
    }
}
