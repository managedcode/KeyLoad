using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeObservationWitness : IPartitionMovementNativeObserver, IDisposable
{
    private const int ExpectedCompletions = 1;
    private const string Unjoined = "The original native peer terminal observation was not joined exactly once.";
    private const string Active = "An original native peer observation remains armed.";
    private const string Report = "NativeMovementObservation nonce={0:N} voterMatched=true stage={1} error={2} authenticated={3} dispatched={4}";
    private static readonly System.Text.CompositeFormat ReportFormat = System.Text.CompositeFormat.Parse(Report);
    private readonly Lock gate = new();
    private bool disposed;
    private Armed? current;

    internal Armed Arm(Guid nonce, string voter)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(nonce, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(voter);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is not null)
            { throw new InvalidOperationException(nameof(Arm)); }
            return current = new(this, nonce, voter);
        }
    }

    public IPartitionMovementNativeObservation? Begin(Guid originalNonce, string receivingVoter)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return current is { } armed && armed.Nonce == originalNonce
                && string.Equals(armed.Voter, receivingVoter, StringComparison.Ordinal) ? armed : null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            var wasArmed = current is not null;
            current?.Dispose();
            disposed = true;
            if (wasArmed)
            { throw new InvalidOperationException(Active); }
        }
        GC.SuppressFinalize(this);
    }

    internal sealed class Armed(ControlledPartitionMovementNativeObservationWitness owner, Guid nonce, string voter)
        : IPartitionMovementNativeObservation, IDisposable
    {
        private PartitionMovementNativeObservationStage stage;
        private Terminal? terminal;
        private int completions;
        internal Guid Nonce => nonce;
        internal string Voter => voter;

        public void Enter(PartitionMovementNativeObservationStage value)
        {
            lock (owner.gate)
            {
                if (terminal is null)
                { stage = value; }
            }
        }

        public void Complete(ErrorCode? error, bool authenticated, bool dispatched)
        {
            lock (owner.gate)
            {
                completions++;
                terminal ??= new(stage, error, authenticated, dispatched);
            }
        }

        internal void RequireAndReport()
            => RequireAndReport(null);

        internal void RequireAndReport(PartitionMovementNativeObservationStage expectedStage, ErrorCode expectedError,
            bool authenticated, bool dispatched)
            => RequireAndReport(new(expectedStage, expectedError, authenticated, dispatched));

        private void RequireAndReport(Terminal? expected)
        {
            lock (owner.gate)
            {
                if (terminal is not { } value || completions != ExpectedCompletions
                    || expected is not null && value != expected)
                { throw new InvalidOperationException(Unjoined); }
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, ReportFormat, nonce,
                    value.Stage, value.Error, value.Authenticated, value.Dispatched));
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (ReferenceEquals(owner.current, this))
                { owner.current = null; }
            }
        }

        private sealed record Terminal(PartitionMovementNativeObservationStage Stage, ErrorCode? Error,
            bool Authenticated, bool Dispatched);
    }
}
