using System.ComponentModel;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

internal static class ClusterContainerUser
{
    private const string ConfigurationKey = "KeyLoad:ContainerUser";
    private const string IdExecutable = "/usr/bin/id";
    private const string UserArgument = "-u";
    private const string GroupArgument = "-g";
    private const string Separator = ":";
    private const string LookupError = "Cannot determine the host container uid:gid. Configure KeyLoad:ContainerUser explicitly.";

    internal static string? Resolve(IDistributedApplicationBuilder builder)
    {
        var options = AppHostOptionsRegistration.Get(builder).Startup;
        if (options.Value.ContainerUser is { } configured && !string.IsNullOrWhiteSpace(configured))
        { return configured; }
        if (OperatingSystem.IsWindows())
        { return null; }
        return Task.Run(() => ReadUnixIdentityAsync(options)).GetAwaiter().GetResult();
    }

    private static async Task<string> ReadUnixIdentityAsync(IOptions<AppHostStartupOptions> options)
    {
        var user = await ReadIdAsync(UserArgument, options.Value);
        var group = await ReadIdAsync(GroupArgument, options.Value);
        return user + Separator + group;
    }

    private static async Task<string> ReadIdAsync(string argument, AppHostStartupOptions policy)
    {
        using var deadline = new CancellationTokenSource(policy.ContainerIdentityLookupTimeout, TimeProvider.System);
        using var process = new Process
        {
            StartInfo = new(IdExecutable)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }
        };
        process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start())
            { throw new InvalidOperationException(LookupError); }
            var output = await ReadOutputAsync(process.StandardOutput, policy.ContainerIdentityOutputCharacters, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0 || !uint.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            { throw new InvalidOperationException(LookupError); }
            return id.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        { throw new InvalidOperationException(LookupError); }
        finally { KillRemaining(process); }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var output = new char[maximumCharacters];
        var length = 0;
        while (length < output.Length)
        {
            var count = await reader.ReadAsync(output.AsMemory(length), cancellationToken);
            if (count == 0)
            { return new(output, 0, length); }
            length += count;
        }
        throw new InvalidOperationException(LookupError);
    }

    private static void KillRemaining(Process process)
    {
        try
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }
        catch (InvalidOperationException) { }
    }
}
