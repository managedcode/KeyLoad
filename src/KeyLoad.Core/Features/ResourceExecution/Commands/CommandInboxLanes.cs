namespace KeyLoad.Core;

/// <summary>Owns the bounded control and data FIFO queues used by one command inbox.</summary>
/// <remarks>Callers serialize every operation with the inbox gate.</remarks>
internal sealed class CommandInboxLanes
{
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;

    private const int MaximumControlBurst = 8;
    private const string SignalMismatchDetail = "The command queue signal is inconsistent.";

    private readonly Queue<AdmittedCommand> commands = [];
    private readonly Queue<AdmittedCommand> controls = [];
    private int controlBurst;

    /// <summary>Adds an admitted command to its FIFO lane.</summary>
    /// <param name="command">The command whose operation determines the lane.</param>
    public void Enqueue(AdmittedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var lane = CommandAdmissionGovernor.IsControl(command.Operation.Kind) ? controls : commands;
        lane.Enqueue(command);
    }

    /// <summary>Returns the next command while enforcing the bounded control burst.</summary>
    /// <param name="stopped">Indicates that no further commands can arrive.</param>
    /// <returns>The next command, or null when stopped and empty.</returns>
    public AdmittedCommand? Dequeue(bool stopped)
    {
        if (controls.Count > EmptyElementCount && (commands.Count == EmptyElementCount || controlBurst < MaximumControlBurst))
        {
            controlBurst = Math.Min(controlBurst + AdjacentElementOffset, MaximumControlBurst);
            return controls.Dequeue();
        }
        if (commands.Count > EmptyElementCount)
        {
            controlBurst = EmptyElementCount;
            return commands.Dequeue();
        }
        if (stopped)
        {
            return null;
        }

        throw new InvalidOperationException(SignalMismatchDetail);
    }

    /// <summary>Fails all queued commands with their retained unknown-outcome detail.</summary>
    public void FailQueued()
    {
        FailLane(commands);
        FailLane(controls);
    }

    private static void FailLane(Queue<AdmittedCommand> queue)
    {
        while (queue.TryDequeue(out var command))
        {
            command.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, CommandAdmissionDetails.UnknownWriteOutcome));
        }
    }
}
