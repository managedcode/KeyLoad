using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Captures native launch evidence at its actual Aspire and operating-system owner.</summary>
internal static class ProjectProcessEvidence
{
    private const string ArgumentsProperty = "executable.args";
    private const string WorkDirectoryProperty = "executable.workdir";
    private const string AssemblySuffix = ".dll";
    private const string SymbolsSuffix = ".pdb";

    internal static NativeProcessEvidence Read(ResourceNotificationService notifications, string resourceName)
    {
        var identity = ProjectProcessIdentity.Read(notifications, resourceName);
        if (!notifications.TryGetCurrentState(resourceName, out var current) || current.Resource is not ProjectResource project)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        using var process = Process.GetProcessById(identity.ProcessId);
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("The native executable is unavailable.");
        var directory = (string)current.Snapshot.Properties.Single(property => property.Name == WorkDirectoryProperty).Value!;
        var arguments = (IEnumerable<string>)current.Snapshot.Properties.Single(property => property.Name == ArgumentsProperty).Value!;
        var managed = ResolveAssembly(project.GetProjectMetadata().ProjectPath, executable, directory, arguments);
        var binary = CaptureBinary(executable, managed);
        if (ProjectProcessIdentity.Read(notifications, resourceName) != identity)
        {
            throw new InvalidOperationException("The native process changed during evidence capture.");
        }
        return new(resourceName, identity.ProcessId, new DateTime(identity.ProcessStartTimeUtcTicks, DateTimeKind.Utc),
            new DateTime(identity.AspireStartTimeUtcTicks, DateTimeKind.Utc), binary);
    }

    internal static NativeProcessEvidence ReadCurrent(string name)
    {
        using var process = Process.GetCurrentProcess();
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("The native executable is unavailable.");
        var assembly = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("The managed entry assembly is unavailable.");
        return new(name, process.Id, process.StartTime.ToUniversalTime(), null, CaptureBinary(executable, assembly));
    }

    internal static void Verify(NativeProcessEvidence expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var process = Process.GetProcessById(expected.ProcessId);
        var executable = process.MainModule?.FileName;
        if (executable is null || process.HasExited || process.StartTime.ToUniversalTime() != expected.StartedAtUtc
            || executable != expected.Binary.ExecutablePath
            || CaptureBinary(executable, expected.Binary.AssemblyPath) != expected.Binary)
        {
            throw new InvalidDataException("The native process or its compiled files differ from the observed identity.");
        }
    }

    private static string ResolveAssembly(string project, string executable, string directory, IEnumerable<string> arguments)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        var expected = name + AssemblySuffix;
        var explicitPaths = arguments.Where(argument => Path.GetFileName(argument) == expected)
            .Select(argument => Path.GetFullPath(argument, directory)).Distinct(StringComparer.Ordinal).ToArray();
        if (explicitPaths.Length == 1)
        {
            return explicitPaths.Single();
        }
        if (Path.GetFileNameWithoutExtension(executable) == name && explicitPaths.Length == 0)
        {
            return Path.ChangeExtension(executable, AssemblySuffix);
        }
        throw new InvalidOperationException("The native project launch does not identify exactly one managed entry assembly.");
    }

    private static NativeBinaryEvidence CaptureBinary(string executable, string assembly)
    {
        var symbols = Path.ChangeExtension(assembly, SymbolsSuffix);
        var source = CompiledSourceEvidence.Read(assembly);
        return new(executable, Hash(executable), assembly, Hash(assembly), symbols, Hash(symbols), source.Count, source.Sha256);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
