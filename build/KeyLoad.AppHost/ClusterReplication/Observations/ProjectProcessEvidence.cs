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
    private const string ExecutableUnavailable = "The native executable is unavailable.";
    private const string EntryAssemblyUnavailable = "The managed entry assembly is unavailable.";
    private const string ProcessChanged = "The native process changed during evidence capture.";
    private const string EvidenceMismatch = "The native process or its compiled files differ from the observed identity.";
    private const string AmbiguousEntryAssembly = "The native project launch does not identify exactly one managed entry assembly.";
    private const int SingleEntryAssembly = 1;
    private const int NoExplicitEntryAssembly = 0;

    internal static NativeProcessEvidence Read(ResourceNotificationService notifications, string resourceName)
    {
        var identity = ProjectProcessIdentity.Read(notifications, resourceName);
        if (!notifications.TryGetCurrentState(resourceName, out var current) || current.Resource is not ProjectResource project)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        using var process = Process.GetProcessById(identity.ProcessId);
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException(ExecutableUnavailable);
        var directory = (string)current.Snapshot.Properties.Single(property => property.Name == WorkDirectoryProperty).Value!;
        var arguments = (IEnumerable<string>)current.Snapshot.Properties.Single(property => property.Name == ArgumentsProperty).Value!;
        var managed = ResolveAssembly(project.GetProjectMetadata().ProjectPath, executable, directory, arguments);
        var binary = CaptureBinary(executable, managed);
        if (ProjectProcessIdentity.Read(notifications, resourceName) != identity)
        {
            throw new InvalidOperationException(ProcessChanged);
        }
        return new(resourceName, identity.ProcessId, new DateTime(identity.ProcessStartTimeUtcTicks, DateTimeKind.Utc),
            new DateTime(identity.AspireStartTimeUtcTicks, DateTimeKind.Utc), binary);
    }

    internal static NativeProcessEvidence ReadCurrent(string name)
    {
        using var process = Process.GetCurrentProcess();
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException(ExecutableUnavailable);
        var assembly = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException(EntryAssemblyUnavailable);
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
            throw new InvalidDataException(EvidenceMismatch);
        }
    }

    private static string ResolveAssembly(string project, string executable, string directory, IEnumerable<string> arguments)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        var expected = name + AssemblySuffix;
        var explicitPaths = arguments.Where(argument => Path.GetFileName(argument) == expected)
            .Select(argument => Path.GetFullPath(argument, directory)).Distinct(StringComparer.Ordinal).ToArray();
        if (explicitPaths.Length == SingleEntryAssembly)
        {
            return explicitPaths.Single();
        }
        if (Path.GetFileNameWithoutExtension(executable) == name && explicitPaths.Length == NoExplicitEntryAssembly)
        {
            return Path.ChangeExtension(executable, AssemblySuffix);
        }
        throw new InvalidOperationException(AmbiguousEntryAssembly);
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
