using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCapabilityLocalProfileTests
{
    private const string ProfileKey = "KeyLoadTests:ClusterRouting:Profile";
    private const string RegistrationKey = "KeyLoadTests:ClusterRouting:RegisterPhysicalOwners";
    private const string ProtectedKey = "KeyLoadTests:ClusterRouting:ProtectedDocumentMovement";
    private const string RemoteDocumentKey = "KeyLoadTests:ClusterRouting:RemoteDocumentReads";
    private const string RemoteQueryKey = "KeyLoadTests:ClusterRouting:RemotePartitionQueries";
    private const string ProvenanceKey = "KEYLOAD_IMAGE_PROVENANCE";
    private const string GithubReceiptKey = "KEYLOAD_IMAGE_RECEIPT";
    private const string GithubSourceKey = "GITHUB_SHA";
    private const string GithubActionsKey = "GITHUB_ACTIONS";
    private const string LocalChildKey = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    private const string LocalReceiptKey = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string Omission = "KeyLoadTests:NativeDiscoveryOmission:";
    private const string Invalid = "NativeDiscoveryOmissionProfileInvalid";
    private const string LocalTag = "local-0123456789abcdef0123456789abcdef";

    [Test]
    public async Task ExactLocalCapabilityProfileRejectsMissingEnrollmentWithoutChangingOwnedFilesThenAdmitsRepair()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            var before = await CaptureAsync(fixture, token).ConfigureAwait(false);
            var builder = Create(fixture);
            var local = LocalDevelopmentContainerImage.Read(builder)!;
            await Assert.That(TwoRf3QueryProbe.Create(builder, fixture.DataRoot, null, local)).IsNotNull();
            await Assert.That(NativeDiscoveryOmissionProfile.Create(builder, fixture.DataRoot, null, local)).IsNotNull();
            foreach (var setting in new[] { RemoteQueryKey,
                ProtectedKey, Omission + "Enabled", Omission })
            {
                var denied = Create(fixture, setting, "false");
                var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                    TwoRf3QueryProbe.Create(denied, fixture.DataRoot, null, local));
                await Assert.That(error.Message).IsEqualTo(setting is Omission or RemoteQueryKey
                    ? "The two-RF3 membership profile is invalid." : Invalid);
                await Assert.That(await EqualAsync(fixture, before, token).ConfigureAwait(false)).IsTrue();
            }
            var runtime = RuntimeContainerImage.Read(RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture),
                RuntimeContainerImage.ServerConfiguration);
            foreach (var image in new RuntimeContainerImage?[] { runtime, null })
            {
                var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                    NativeDiscoveryOmissionProfile.Create(builder, fixture.DataRoot, image,
                        image is null ? null : local));
                await Assert.That(error.Message).IsEqualTo(Invalid);
            }
            await Assert.That(NativeDiscoveryOmissionProfile.Create(Create(fixture), fixture.DataRoot, null,
                LocalDevelopmentContainerImage.Read(Create(fixture)))).IsNotNull();
            await Assert.That(await EqualAsync(fixture, before, token).ConfigureAwait(false)).IsTrue();
        }).ConfigureAwait(false);
    }

    private static IDistributedApplicationBuilder Create(RequestCqrsProbeAppHostFileFixture fixture,
        string? changed = null, string? value = null)
    {
        var builder = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture);
        var values = new Dictionary<string, string?>
        {
            [ProfileKey] = "two-rf3",
            [RegistrationKey] = "true",
            [ProtectedKey] = "true",
            [RemoteDocumentKey] = "true",
            [RemoteQueryKey] = "true",
            [Omission + "Enabled"] = "true",
            [Omission + "Root"] = fixture.Root,
            [Omission + "SessionId"] = fixture.SessionId,
            [RuntimeContainerImage.ServerConfiguration] = "keyload/local-server:" + LocalTag,
            [ProvenanceKey] = "local-development",
            [GithubReceiptKey] = null,
            [GithubSourceKey] = null,
            [GithubActionsKey] = null,
            [LocalChildKey] = "true",
            [LocalReceiptKey] = "TestResults/rf3/local-images/image-0123456789abcdef0123456789abcdef.json"
        };
        if (changed == Omission)
        { values.Remove(Omission + "Enabled"); values.Remove(Omission + "Root"); values.Remove(Omission + "SessionId"); }
        else if (changed is not null)
        { values[changed] = value; }
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    private static async Task<Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>> CaptureAsync(RequestCqrsProbeAppHostFileFixture fixture,
        CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private mode fixtures require a Unix host."); }
        var result = new Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(fixture.Parent, "*", SearchOption.AllDirectories))
        { result.Add(path, (await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), File.GetUnixFileMode(path))); }
        return result;
    }

    private static async Task<bool> EqualAsync(RequestCqrsProbeAppHostFileFixture fixture,
        Dictionary<string, (byte[] Bytes, UnixFileMode Mode)> before, CancellationToken token)
    {
        var after = await CaptureAsync(fixture, token).ConfigureAwait(false);
        return before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var bytes)
            && pair.Value.Mode == bytes.Mode && pair.Value.Bytes.AsSpan().SequenceEqual(bytes.Bytes));
    }
}
