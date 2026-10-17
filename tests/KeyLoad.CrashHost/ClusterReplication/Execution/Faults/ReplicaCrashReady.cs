using KeyLoad.Replication;

namespace KeyLoad.CrashHost;

/// <summary>The flushed readiness receipt proves exactly which physical process boundary was reached.</summary>
/// <param name="Boundary">The durable boundary at which the child remains paused.</param>
/// <param name="Incarnation">The fixed incarnation shared by the source and target.</param>
/// <param name="NodeId">The target's persisted physical identity.</param>
internal sealed record ReplicaCrashReady(ReplicaCrashBoundary Boundary, Guid Incarnation, Guid NodeId);
