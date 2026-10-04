using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed class AdminBrowserProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly string profile;
    private readonly Task errors;
    private readonly Task output;
    internal AdminBrowserCdp Cdp { get; } = new();

    private AdminBrowserProcess(Process process, string profile)
    {
        this.process = process;
        this.profile = profile;
        errors = DrainAsync(process.StandardError);
        output = DrainAsync(process.StandardOutput);
    }

    internal static async Task<AdminBrowserProcess> StartAsync(CancellationToken cancellationToken)
    {
        var executable = Environment.GetEnvironmentVariable(AdminBrowserProtocol.BrowserEnvironment);
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        { throw new InvalidOperationException(AdminBrowserProtocol.MissingBrowser); }
        var profile = Path.Combine(Path.GetTempPath(), AdminBrowserProtocol.ProfilePrefix + Guid.NewGuid().ToString(AdminBrowserProtocol.GuidFormat));
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--headless=new", "--no-sandbox", "--disable-dev-shm-usage",
            "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank" })
        { start.ArgumentList.Add(argument); }
        var process = Process.Start(start) ?? throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure);
        var owner = new AdminBrowserProcess(process, profile);
        try
        {
            var endpoint = await owner.EndpointAsync(cancellationToken);
            await owner.Cdp.ConnectAsync(endpoint, cancellationToken);
            return owner;
        }
        catch (Exception)
        { await owner.DisposeAsync(); throw; }
    }

    private async Task<Uri> EndpointAsync(CancellationToken cancellationToken)
    {
        var endpointFile = Path.Combine(profile, AdminBrowserProtocol.EndpointFile);
        while (!File.Exists(endpointFile))
        {
            if (process.HasExited)
            { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
            await Task.Delay(AdminBrowserProtocol.PollMilliseconds, cancellationToken);
        }
        var lines = await File.ReadAllLinesAsync(endpointFile, cancellationToken);
        var port = int.Parse(lines[0], CultureInfo.InvariantCulture);
        using var http = new HttpClient { Timeout = AdminBrowserProtocol.Deadline };
        using var targets = JsonDocument.Parse(await http.GetStringAsync(new Uri(
            AdminBrowserProtocol.LoopbackPrefix + port.ToString(CultureInfo.InvariantCulture) + AdminBrowserProtocol.TargetsPath), cancellationToken));
        return new(targets.RootElement.EnumerateArray().First(target => target.GetProperty(AdminBrowserProtocol.TypeProperty).GetString() == AdminBrowserProtocol.PageType)
            .GetProperty(AdminBrowserProtocol.SocketProperty).GetString()!);
    }

    public async ValueTask DisposeAsync()
    {
        await Cdp.DisposeAsync();
        try
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(errors, output);
        }
        finally
        {
            process.Dispose();
            if (Directory.Exists(profile))
            { Directory.Delete(profile, recursive: true); }
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[AdminBrowserProtocol.ReceiveBufferBytes];
        while (await reader.ReadAsync(buffer.AsMemory()) > 0)
        { /* Drain the genuine process without retaining unbounded diagnostic data. */ }
    }
}
