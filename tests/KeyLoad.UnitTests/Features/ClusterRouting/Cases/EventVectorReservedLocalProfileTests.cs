using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class EventVectorReservedLocalProfileTests
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
    private const string Selection = "KeyLoadTests:EventVectorReservedProbe";
    private const string Invalid = "EventVectorReservedProbeProfileInvalid";
    private const string LocalTag = "local-0123456789abcdef0123456789abcdef";

    [Test]
    public async Task ExactEventVectorReservedProfileRefusesForeignEnrollmentWithoutEffectsThenAdmitsSameOwnedProfile()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            var before = await CaptureAsync(fixture, token).ConfigureAwait(false);
            var builder = Create(fixture);
            var local = LocalDevelopmentContainerImage.Read(builder)!;
            await Assert.That(TwoRf3QueryProbe.Create(builder, fixture.DataRoot, null, local)).IsNotNull();
            foreach (var setting in new[] { ProtectedKey, RegistrationKey, Selection })
            {
                var denied = Create(fixture, setting, "false");
                var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                    TwoRf3QueryProbe.Create(denied, fixture.DataRoot, null, local));
                await Assert.That(error.Message).IsEqualTo(Invalid);
                await Assert.That(await EqualAsync(fixture, before, token).ConfigureAwait(false)).IsTrue();
            }
            var absent = Create(fixture, Selection, null);
            var ordinary = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TwoRf3QueryProbe.Create(absent, fixture.DataRoot, null, local));
            await Assert.That(ordinary.Message).IsEqualTo("The two-RF3 membership profile is invalid.");
            var nested = Create(fixture, Selection + ":Foreign", "true");
            var malformed = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TwoRf3QueryProbe.Create(nested, fixture.DataRoot, null, local));
            await Assert.That(malformed.Message).IsEqualTo(Invalid);
            await Assert.That(TwoRf3QueryProbe.Create(Create(fixture), fixture.DataRoot, null,
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
            [Selection] = "true",
            [RuntimeContainerImage.ServerConfiguration] = "keyload/local-server:" + LocalTag,
            [ProvenanceKey] = "local-development",
            [GithubReceiptKey] = null,
            [GithubSourceKey] = null,
            [GithubActionsKey] = null,
            [LocalChildKey] = "true",
            [LocalReceiptKey] = "TestResults/rf3/local-images/image-0123456789abcdef0123456789abcdef.json"
        };
        if (changed is not null)
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
