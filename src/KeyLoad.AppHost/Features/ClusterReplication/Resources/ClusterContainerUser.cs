using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

internal static class ClusterContainerUser
{
    private const string IdExecutable = "/usr/bin/id";
    private const string UserArgument = "-u";
    private const string GroupArgument = "-g";
    private const string Separator = ":";
    private const string LookupError = "Cannot determine the host container uid:gid. Configure KeyLoad:ContainerUser explicitly.";

    internal static string? Resolve(IDistributedApplicationBuilder builder, TimeProvider timeProvider)
    {
        var options = AppHostOptionsRegistration.Get(builder).Startup;
        if (options.Value.ContainerUser is { } configured && !string.IsNullOrWhiteSpace(configured))
        { return configured; }
        if (OperatingSystem.IsWindows())
        { return null; }
        return Task.Run(() => ReadUnixIdentityAsync(options, timeProvider)).GetAwaiter().GetResult();
    }

    private static async Task<string> ReadUnixIdentityAsync(IOptions<AppHostStartupOptions> options, TimeProvider timeProvider)
    {
        var user = await ReadIdAsync(UserArgument, options.Value, timeProvider);
        var group = await ReadIdAsync(GroupArgument, options.Value, timeProvider);
        return user + Separator + group;
    }

    private static async Task<string> ReadIdAsync(string argument, AppHostStartupOptions policy, TimeProvider timeProvider)
    {
        const int EmptyValue = 0;

        using var deadline = new CancellationTokenSource(policy.ContainerIdentityLookupTimeout, timeProvider);
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
            if (process.ExitCode != EmptyValue || !uint.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            { throw new InvalidOperationException(LookupError); }
            return id.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        { throw new InvalidOperationException(LookupError); }
        finally { KillRemaining(process); }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        const int LengthInitialValue = 0;
        const int EmptyValue = 0;
        const int StartIndexValue = 0;

        var output = new char[maximumCharacters];
        var length = LengthInitialValue;
        while (length < output.Length)
        {
            var count = await reader.ReadAsync(output.AsMemory(length), cancellationToken);
            if (count == EmptyValue)
            { return new(output, StartIndexValue, length); }
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
