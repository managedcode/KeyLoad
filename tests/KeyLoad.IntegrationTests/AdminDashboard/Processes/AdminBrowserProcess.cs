using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed class AdminBrowserProcess : IAsyncDisposable
{
    private const string UnknownSettlement = "The original administration browser process and readers have not joined.";
    private readonly Lock disposalGate = new();
    private Process? process;
    private Task? disposal;
    private AdminBrowserCdp? cdp;
    internal AdminBrowserCdp Cdp => cdp ?? throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure);
    internal string ProfilePath
    {
        get;
    }
    internal Task? OriginalExit
    {
        get; private set;
    }
    internal Task? OriginalOutput
    {
        get; private set;
    }
    internal Task? OriginalError
    {
        get; private set;
    }
    internal bool PreserveEvidence
    {
        get; set;
    }

    private AdminBrowserProcess(string profile) => ProfilePath = profile;

    internal static async Task<AdminBrowserProcess> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = Environment.GetEnvironmentVariable(AdminBrowserProtocol.BrowserEnvironment);
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException(AdminBrowserProtocol.MissingBrowser);
        }
        var profile = Path.Combine(Path.GetTempPath(), AdminBrowserProtocol.ProfilePrefix + Guid.NewGuid().ToString(AdminBrowserProtocol.GuidFormat));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--headless=new", "--no-sandbox", "--disable-dev-shm-usage",
            "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank" })
        {
            start.ArgumentList.Add(argument);
        }
        var owner = new AdminBrowserProcess(profile);
        try
        {
            Directory.CreateDirectory(profile);
            owner.cdp = new AdminBrowserCdp();
            owner.process = Process.Start(start) ?? throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure);
            owner.OriginalExit = owner.process.WaitForExitAsync(CancellationToken.None);
            owner.OriginalError = DrainAsync(owner.process.StandardError);
            owner.OriginalOutput = DrainAsync(owner.process.StandardOutput);
            var endpoint = await owner.EndpointAsync(cancellationToken);
            await owner.Cdp.ConnectAsync(endpoint, cancellationToken);
            return owner;
        }
        catch (Exception primary)
        {
            owner.PreserveEvidence = true;
            var cleanup = await OwnedProcessFailureObserver.CaptureAsync(owner.DisposeAsync().AsTask()).ConfigureAwait(false);
            if (cleanup is not null)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }

    private async Task<Uri> EndpointAsync(CancellationToken cancellationToken)
    {
        var original = process ?? throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure);
        var endpointFile = Path.Combine(ProfilePath, AdminBrowserProtocol.EndpointFile);
        while (!File.Exists(endpointFile))
        {
            if (original.HasExited)
            {
                throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(AdminBrowserProtocol.PollMilliseconds), TimeProvider.System, cancellationToken);
        }
        var lines = await File.ReadAllLinesAsync(endpointFile, cancellationToken);
        var port = int.Parse(lines[0], CultureInfo.InvariantCulture);
        using var http = new HttpClient { Timeout = AdminBrowserProtocol.Deadline };
        using var targets = JsonDocument.Parse(await http.GetStringAsync(new Uri(
            AdminBrowserProtocol.LoopbackPrefix + port.ToString(CultureInfo.InvariantCulture) + AdminBrowserProtocol.TargetsPath), cancellationToken));
        return new(targets.RootElement.EnumerateArray().First(target => target.GetProperty(AdminBrowserProtocol.TypeProperty).GetString() == AdminBrowserProtocol.PageType)
            .GetProperty(AdminBrowserProtocol.SocketProperty).GetString()!);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        var cdpFailure = await OwnedProcessFailureObserver.CaptureAsync(DisposeCdpAsync()).ConfigureAwait(false);
        if (cdpFailure is not null)
        {
            failures.Add(cdpFailure);
        }
        var joined = process is null;
        if (process is { } original)
        {
            var cleanup = LocalImageOwnedProcessLifetime.TerminateAndJoinAsync(original, OriginalExit, OriginalOutput, OriginalError, failures);
            var failure = await OwnedProcessFailureObserver.CaptureAsync(cleanup).ConfigureAwait(false);
            if (failure is not null)
            {
                failures.Add(failure);
            }
            var exited = false;
            OwnedProcessFailureObserver.Observe(() => exited = original.HasExited, failures);
            joined = exited && (OriginalExit is null || OriginalExit.IsCompleted)
                && (OriginalOutput is null || OriginalOutput.IsCompleted) && (OriginalError is null || OriginalError.IsCompleted);
            if (joined)
            {
                OwnedProcessFailureObserver.Observe(original.Dispose, failures);
            }
        }
        if (!joined)
        {
            failures.Add(new InvalidOperationException(UnknownSettlement));
        }
        if (joined && failures.Count == 0 && !PreserveEvidence)
        {
            OwnedProcessFailureObserver.Observe(() =>
            {
                if (Directory.Exists(ProfilePath))
                {
                    Directory.Delete(ProfilePath, recursive: true);
                }
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task DisposeCdpAsync()
    {
        if (cdp is not null)
        {
            await cdp.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[AdminBrowserProtocol.ReceiveBufferBytes];
        while (await reader.ReadAsync(buffer.AsMemory()) > 0)
        { /* Drain the genuine process without retaining unbounded diagnostic data. */
        }
    }
}
