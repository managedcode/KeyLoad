using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Captures execution launch evidence at its actual Aspire and operating-system owner.</summary>
internal static class ProjectProcessEvidence
{
    private const string SymbolsUnavailable = "The observed managed assembly has no matching portable symbols.";
    private const string SymbolsPathMismatch = "The observed managed assembly requires its matching symbol file.";
    private const string SymbolsSuffix = ".pdb";
    private const string WorkDirectoryProperty = "executable.workDir";
    private const string ExecutableUnavailable = "The execution executable is unavailable.";
    private const string EntryAssemblyUnavailable = "The managed entry assembly is unavailable.";
    private const string ProcessChanged = "The execution process changed during evidence capture.";
    private const string EvidenceMismatch = "The execution process or its compiled files differ from the observed identity.";

    internal static async Task<ProcessEvidence> ReadAsync(ResourceNotificationService notifications,
        string resourceName, IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        var original = ProjectProcessIdentity.ReadLaunch(notifications, resourceName);
        if (!notifications.TryGetCurrentState(resourceName, out var current) || current.Resource is not ProjectResource project)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        using var launch = Process.GetProcessById(original.ProcessId);
        var dotnet = launch.MainModule?.FileName ?? throw new InvalidOperationException(ExecutableUnavailable);
        var directory = (string)current.Snapshot.Properties.Single(property => property.Name == WorkDirectoryProperty).Value!;
        var metadata = await ProjectLaunchMetadata.ReadAsync(project.GetProjectMetadata().ProjectPath,
            directory, dotnet, options, token).ConfigureAwait(false);
        var serverProcessId = await ProjectProcessChild.ReadAsync(original.ProcessId, metadata.Executable, options, token)
            .ConfigureAwait(false);
        var identity = await ProjectProcessIdentity.ReadAsync(notifications, resourceName, serverProcessId, options, token)
            .ConfigureAwait(false);
        if (identity.Launch != original)
        {
            throw new InvalidOperationException(ProcessChanged);
        }
        using var process = Process.GetProcessById(identity.ProcessId);
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException(ExecutableUnavailable);
        if (!string.Equals(executable, metadata.Executable, StringComparison.Ordinal))
        {
            throw new InvalidDataException(EvidenceMismatch);
        }
        var binary = CaptureBinary(executable, metadata.Assembly);
        if (await ProjectProcessIdentity.ReadAsync(notifications, resourceName, serverProcessId, options, token)
            .ConfigureAwait(false) != identity)
        {
            throw new InvalidOperationException(ProcessChanged);
        }
        return new(resourceName, identity.ProcessId, new DateTime(identity.ProcessStartTimeUtcTicks, DateTimeKind.Utc),
            new DateTime(identity.AspireStartTimeUtcTicks, DateTimeKind.Utc), identity.LaunchProcessId,
            new DateTime(identity.LaunchProcessStartTimeUtcTicks, DateTimeKind.Utc), binary);
    }

    internal static ProcessEvidence ReadCurrent(string name)
    {
        using var process = Process.GetCurrentProcess();
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException(ExecutableUnavailable);
        var assembly = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException(EntryAssemblyUnavailable);
        return new(name, process.Id, process.StartTime.ToUniversalTime(), null, null, null, CaptureBinary(executable, assembly));
    }

    internal static void Verify(ProcessEvidence expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var process = Process.GetProcessById(expected.ProcessId);
        var executable = process.MainModule?.FileName;
        if (executable is null || process.HasExited || process.StartTime.ToUniversalTime() != expected.StartedAtUtc
            || executable != expected.Binary.ExecutablePath
            || CaptureBinary(executable, expected.Binary.AssemblyPath) != expected.Binary)
        {
            throw new InvalidDataException(EvidenceMismatch);
        }
    }

    private static BinaryEvidence CaptureBinary(string executable, string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var portable = new PEReader(stream);
        if (!portable.TryOpenAssociatedPortablePdb(assembly, File.OpenRead, out var provider, out var symbols))
        {
            throw new InvalidDataException(SymbolsUnavailable);
        }
        using (provider)
        {
            if (symbols is null || !string.Equals(Path.GetFullPath(symbols),
                Path.GetFullPath(Path.ChangeExtension(assembly, SymbolsSuffix)), StringComparison.Ordinal))
            {
                throw new InvalidDataException(SymbolsPathMismatch);
            }
            var source = CompiledSourceEvidence.Read(assembly);
            return new(executable, Hash(executable), assembly, Hash(assembly), symbols, Hash(symbols), source.Count, source.Sha256);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
