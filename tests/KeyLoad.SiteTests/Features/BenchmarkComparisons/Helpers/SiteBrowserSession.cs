using System.ComponentModel;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBrowserSession : IAsyncDisposable
{
    private readonly SiteTempDirectory temporary;
    private readonly SiteStaticFileHost host;
    public SiteBrowserChrome Chrome { get; }
    private readonly string sessionDirectory;
    private readonly SiteBrowserCoverageMetadata metadata;
    private bool completed;

    internal SiteBrowserSession(SiteTempDirectory temporary, SiteStaticFileHost host, SiteBrowserChrome chrome,
        string sessionDirectory, SiteBrowserCoverageMetadata metadata)
    {
        this.temporary = temporary;
        this.host = host;
        Chrome = chrome;
        this.sessionDirectory = sessionDirectory;
        this.metadata = metadata;
    }

    public string BaseUrl => host.BaseUrl;
    public string Output => temporary.Output;

    internal static async Task CleanupStartupFailure(SiteBrowserChrome? chrome, SiteStaticFileHost? host,
        SiteTempDirectory temporary)
    {
        if (chrome is not null)
        {
            try
            { await chrome.DisposeAsync(); }
            catch (Exception cleanupException) when (cleanupException is WebSocketException or
                OperationCanceledException or InvalidOperationException or Win32Exception or TimeoutException or
                ObjectDisposedException or IOException)
            { /* Preserve the startup failure. */ }
        }
        if (host is not null)
        {
            try
            { await host.DisposeAsync(); }
            catch (Exception cleanupException) when (cleanupException is OperationCanceledException or
                HttpListenerException or ObjectDisposedException or IOException)
            { /* Preserve the startup failure. */ }
        }
        try
        { await temporary.DisposeAsync(); }
        catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
        { /* Preserve the startup failure. */ }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        var errors = Chrome.ReadErrors();
        if (errors.Count != SiteTokens.Zero)
        {
            throw new InvalidOperationException(SiteBrowserTokens.ErrorStatus + " " + errors[SiteTokens.Zero].Message);
        }
        var coverage = await Chrome.CompleteCoverageAsync(cancellationToken);
        var finalErrors = Chrome.ReadErrors();
        if (finalErrors.Count != SiteTokens.Zero)
        {
            throw new InvalidOperationException(SiteBrowserTokens.ErrorStatus + " " + finalErrors[SiteTokens.Zero].Message);
        }
        await Chrome.DisposeAsync();
        await host.DisposeAsync();
        Directory.CreateDirectory(sessionDirectory);
        var coverageNames = new List<string>(coverage.Count);
        foreach (var snapshot in coverage)
        {
            var coverageName = SiteBrowserTokens.NativeCoveragePrefix + Guid.NewGuid().ToString("N") + SiteBrowserTokens.NativeCoverageSuffix;
            await File.WriteAllBytesAsync(Path.Combine(sessionDirectory, coverageName),
                JsonSerializer.SerializeToUtf8Bytes(snapshot), cancellationToken);
            coverageNames.Add(coverageName);
        }
        var completedMetadata = metadata with { CoverageFiles = coverageNames.ToArray() };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(completedMetadata, SiteCoverageTokens.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(sessionDirectory, SiteBrowserTokens.MetadataFile), bytes, cancellationToken);
        completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!completed)
        {
            try
            { await Chrome.DisposeAsync(); }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or
                InvalidOperationException or Win32Exception or TimeoutException or ObjectDisposedException or IOException)
            { /* Preserve the test failure. */ }
            try
            { await host.DisposeAsync(); }
            catch (Exception exception) when (exception is OperationCanceledException or HttpListenerException or
                ObjectDisposedException or IOException)
            { /* Preserve the test failure. */ }
            try
            {
                if (Directory.Exists(sessionDirectory))
                {
                    Directory.Delete(sessionDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { /* Preserve the test failure. */ }
        }
        try
        { await temporary.DisposeAsync(); }
        catch (Exception exception) when (!completed && exception is IOException or UnauthorizedAccessException)
        { /* Preserve the test failure. */ }
    }

    internal static async Task<SiteCoverageSourceManifest> ReadManifest(SiteTestInputs inputs, string coverageRoot,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(coverageRoot), SiteBrowserTokens.SourceManifestFile);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserCoverageMissing);
        }
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<SiteCoverageSourceManifest>(stream,
            SiteCoverageTokens.JsonOptions, cancellationToken);
        if (manifest is null || manifest.SchemaVersion != SiteCoverageTokens.Schema ||
            manifest.SourceRevision != inputs.SiteRevision || manifest.Sources.Count != SiteCoverageTokens.ProductionSources.Length ||
            !manifest.Sources.Select(source => source.Path).SequenceEqual(
                SiteCoverageTokens.ProductionSources.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserCoverageMismatch);
        }
        foreach (var path in SiteCoverageTokens.ProductionSources)
        {
            var entry = manifest.Sources.Single(source => source.Path == path);
            var sourcePath = Path.Combine(inputs.Repository, path.Replace('/', Path.DirectorySeparatorChar));
            if (entry.Sha256.Length != SiteCoverageTokens.ShaLength ||
                SiteCoverageSourceManifestWriter.Hash(await File.ReadAllBytesAsync(sourcePath, cancellationToken)) != entry.Sha256)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserCoverageMismatch);
            }
        }
        return manifest;
    }
}

internal sealed class SiteBrowserStartupResources : IAsyncDisposable
{
    private SiteTempDirectory? temporary;
    private SiteStaticFileHost? host;
    private SiteBrowserChrome? chrome;

    private SiteBrowserStartupResources() { }

    public string Path => RequiredTemporary.Path;
    public string Output => RequiredTemporary.Output;
    public SiteStaticFileHost Host => host ?? throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);

    private SiteTempDirectory RequiredTemporary => temporary ??
        throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);

    public static SiteBrowserStartupResources Create() => new();

    public void CreateTemporary() => temporary = SiteTempDirectory.Create();

    public void StartHost() => host = SiteStaticFileHost.Start(Output);

    public async Task<SiteBrowserChrome> StartChromeAsync(string browserPath, string profilePath,
        CancellationToken cancellationToken)
    {
        chrome = await SiteBrowserChrome.StartAsync(browserPath, profilePath, cancellationToken);
        return chrome;
    }

    public SiteBrowserSession Transfer(string sessionDirectory, SiteBrowserCoverageMetadata metadata)
    {
        var session = new SiteBrowserSession(RequiredTemporary, Host,
            chrome ?? throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure),
            sessionDirectory, metadata);
        temporary = null;
        host = null;
        chrome = null;
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        if (temporary is not null)
        {
            await SiteBrowserSession.CleanupStartupFailure(chrome, host, temporary);
        }
    }
}

internal sealed record SiteBrowserCoverageSource(string Path, string Sha256);

internal sealed record SiteBrowserCoverageMetadata(int SchemaVersion, string SourceRevision, string BrowserVersion,
    string[] Origins, SiteBrowserCoverageSource[] Sources, string[] CoverageFiles)
{
    public static SiteBrowserCoverageMetadata Create(string revision, string browserVersion, string origin,
        SiteCoverageSourceManifest manifest)
    {
        var sources = manifest.Sources.Select(source => new SiteBrowserCoverageSource(source.Path, source.Sha256)).ToArray();
        return new(SiteCoverageTokens.Schema, revision, browserVersion,
            [origin.TrimEnd(SiteTokens.UrlPathSeparatorCharacter)], sources, []);
    }
}
