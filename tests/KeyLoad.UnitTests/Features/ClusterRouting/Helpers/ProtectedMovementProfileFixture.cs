using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ProtectedMovementProfileFixture
{
    internal const string False = "false";
    internal const string True = "true";
    internal const string MissingProfile = "The two-RF3 membership profile is invalid.";
    internal const string Foreign = ":Foreign";
    private const string ProvenanceKey = "KEYLOAD_IMAGE_PROVENANCE";
    private const string GithubReceiptKey = "KEYLOAD_IMAGE_RECEIPT";
    private const string GithubSourceKey = "GITHUB_SHA";
    private const string GithubActionsKey = "GITHUB_ACTIONS";
    private const string LocalChildKey = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    private const string LocalReceiptKey = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string Provenance = "local-development";
    private const string RepositoryPrefix = "keyload/local-server:";
    private const string Glob = "*";
    private const string PlatformRefusal = "Private mode fixtures require a Unix host.";
    private const string LocalTag = "local-0123456789abcdef0123456789abcdef";
    private const string Receipt = "TestResults/rf3/local-images/image-0123456789abcdef0123456789abcdef.json";

    internal static IDistributedApplicationBuilder Create(RequestCqrsProbeAppHostFileFixture fixture,
        string? changed = null, string? value = null)
    {
        var builder = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture);
        var values = new Dictionary<string, string?>
        {
            [TwoRf3ProfileProtocol.Setting] = TwoRf3ProfileProtocol.Profile,
            [TwoRf3ProfileProtocol.RegistrationSetting] = True,
            [TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting] = True,
            [TwoRf3ProfileProtocol.RemoteDocumentSetting] = True,
            [TwoRf3ProfileProtocol.RemoteQuerySetting] = True,
            [ProtectedMovementProbeProfile.Setting] = True,
            [RuntimeContainerImage.ServerConfiguration] = RepositoryPrefix + LocalTag,
            [ProvenanceKey] = Provenance,
            [GithubReceiptKey] = null,
            [GithubSourceKey] = null,
            [GithubActionsKey] = null,
            [LocalChildKey] = True,
            [LocalReceiptKey] = Receipt
        };
        if (changed is not null)
        {
            values[changed] = value;
        }
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    internal static Dictionary<string, (byte[] Bytes, UnixFileMode Mode)> Capture(
        RequestCqrsProbeAppHostFileFixture fixture)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(PlatformRefusal);
        }
        var files = new Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(fixture.Parent, Glob, SearchOption.AllDirectories))
        {
            files.Add(path, (File.ReadAllBytes(path), File.GetUnixFileMode(path)));
        }
        return files;
    }

    internal static bool Conserved(RequestCqrsProbeAppHostFileFixture fixture,
        Dictionary<string, (byte[] Bytes, UnixFileMode Mode)> before)
    {
        var after = Capture(fixture);
        return before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var actual)
            && pair.Value.Mode == actual.Mode && pair.Value.Bytes.AsSpan().SequenceEqual(actual.Bytes));
    }
}
