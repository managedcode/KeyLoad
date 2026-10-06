using System.Text;
using KeyLoad.Replication;

namespace KeyLoad.CrashHost;

/// <summary>Real-process replica boundary scenarios; the parent kills the paused process.</summary>
internal static class ReplicaCrashScenario
{
    private const string Scenario = "replica";
    private const string InvalidArguments = "Expected replica, root directory, durable boundary and incarnation.";
    private const string BoundaryNotReached = "The requested durable replica boundary was not reached.";
    private const int ArgumentCount = 4;

    /// <summary>Dispatches replica arguments, or returns false so the shared host can dispatch another scenario.</summary>
    /// <param name="args">The original replica mode, root, boundary and incarnation arguments.</param>
    public static Task<bool> TryRunAsync(string[] args)
    {
        const int NoArguments = 0;
        const int ScenarioArgument = 0;
        const int RootArgument = 1;
        const int BoundaryArgument = 2;
        const int IncarnationArgument = 3;

        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == NoArguments || args[ScenarioArgument] != Scenario)
        {
            return Task.FromResult(false);
        }
        if (args.Length != ArgumentCount || string.IsNullOrWhiteSpace(args[RootArgument])
            || !Enum.TryParse<ReplicaCrashBoundary>(args[BoundaryArgument], out var boundary) || !Enum.IsDefined(boundary)
            || !Guid.TryParse(args[IncarnationArgument], out var incarnation) || incarnation == Guid.Empty)
        {
            throw new ArgumentException(InvalidArguments, nameof(args));
        }
        Run(args[RootArgument], boundary, incarnation);
        throw new InvalidOperationException(BoundaryNotReached);
    }

    private static void Run(string root, ReplicaCrashBoundary boundary, Guid incarnation)
    {
        const int SeedCut = 2;
        const int NextTerm = 2;
        const int NextOperationIndex = 3;
        const int SeedTerm = 1;

        var armed = false;
        ReplicaCrashNode? physicalNode = null;
        using var target = ReplicaCrashNode.OpenTarget(root, incarnation, observed =>
        {
            if (armed && observed == boundary)
            {
                Pause(new(boundary, incarnation, physicalNode!.Canonical.Identity.NodeId));
            }
        });
        physicalNode = target;
        if (ReplicaCrashNode.RequiresSourceStores(boundary))
        {
            armed = true;
            ReplicaCrashTransfer.Run(root, incarnation, target, boundary);
            return;
        }
        target.Populate(SeedCut);
        if (boundary == ReplicaCrashBoundary.TermSaved)
        {
            armed = true;
            target.Log.SaveTermAndVote(NextTerm, ReplicaCrashNode.CandidateId);
            return;
        }
        armed = boundary == ReplicaCrashBoundary.EntryAcknowledged;
        target.Log.Append([new(NextOperationIndex, SeedTerm, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(NextOperationIndex)))]);
        armed = true;
        target.Log.Commit(NextOperationIndex);
    }

    private static void Pause(ReplicaCrashReady ready)
    {
        Console.WriteLine(Encoding.UTF8.GetString(JsonDefaults.Serialize(ready)));
        Console.Out.Flush();
        using var paused = new ManualResetEventSlim(false);
        paused.Wait();
    }
}

/// <summary>The flushed readiness receipt proves exactly which physical process boundary was reached.</summary>
/// <param name="Boundary">The durable boundary at which the child remains paused.</param>
/// <param name="Incarnation">The fixed incarnation shared by the source and target.</param>
/// <param name="NodeId">The target's persisted physical identity.</param>
internal sealed record ReplicaCrashReady(ReplicaCrashBoundary Boundary, Guid Incarnation, Guid NodeId);
