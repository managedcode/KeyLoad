using System.Diagnostics;
using System.Text;
using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaProcessTrial : IAsyncDisposable
{
    private const string Prefix = "keyload-replica-process-";
    private const string IncarnationFormat = "D";
    private const string Executable = "dotnet";
    private const string Scenario = "replica";
    private const string MissingProcess = "The real replica helper process did not start.";
    private const string WrongReceipt = "The replica helper did not acknowledge the requested durable boundary: ";
    private const int DiagnosticLimit = 8_192;
    private const int DiagnosticBuffer = 1_024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly Process process;
    private readonly Task<string> diagnostics;

    private ReplicaProcessTrial(ReplicaCrashBoundary boundary)
    {
        DirectoryPath = ReplicaFixturePaths.NewDirectory(Prefix);
        Incarnation = Guid.NewGuid();
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { typeof(ReplicaCrashScenario).Assembly.Location, Scenario, DirectoryPath,
                     boundary.ToString(), Incarnation.ToString(IncarnationFormat) })
        {
            start.ArgumentList.Add(argument);
        }
        process = Process.Start(start) ?? throw new InvalidOperationException(MissingProcess);
        diagnostics = CaptureDiagnosticsAsync(process.StandardError);
    }

    internal string DirectoryPath { get; }
    internal Guid Incarnation { get; }
    internal ReplicaCrashReady Ready { get; private set; } = null!;

    internal static ReplicaProcessTrial Start(ReplicaCrashBoundary boundary) => new(boundary);

    internal async Task KillAsync(ReplicaCrashBoundary boundary, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        await KillAtBoundaryAsync(boundary, linked.Token);
    }

    private async Task KillAtBoundaryAsync(ReplicaCrashBoundary boundary, CancellationToken cancellationToken)
    {
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException(WrongReceipt + await diagnostics.WaitAsync(cancellationToken));
        Ready = JsonDefaults.Deserialize<ReplicaCrashReady>(Encoding.UTF8.GetBytes(line));
        if (Ready.Boundary != boundary || Ready.Incarnation != Incarnation || Ready.NodeId == Guid.Empty || process.HasExited)
        {
            throw new InvalidOperationException(WrongReceipt + line);
        }
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken);
        await ReplicaProcessFiles.WaitForOwnershipAsync(DirectoryPath, cancellationToken, boundary);
        ReplicaProcessFiles.WriteEvidence(Ready, process.Id, process.ExitCode);
    }

    private static async Task<string> CaptureDiagnosticsAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[DiagnosticBuffer];
        while (true)
        {
            var count = await reader.ReadAsync(buffer);
            if (count == 0)
            { break; }
            var retained = Math.Min(count, DiagnosticLimit - output.Length);
            if (retained > 0)
            { output.Append(buffer, 0, retained); }
        }
        return output.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(deadline.Token);
        }
        var errors = await diagnostics.WaitAsync(deadline.Token);
        if (errors.Length > 0)
        {
            await Console.Error.WriteLineAsync(errors);
        }
        process.Dispose();
        await ReplicaProcessFiles.DeleteAsync(DirectoryPath, deadline.Token);
    }

}
