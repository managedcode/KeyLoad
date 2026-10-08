namespace KeyLoad.Orleans;

/// <summary>Opt-in current silo routing hint; persisted ownership remains independently verified.</summary>
public interface IPhysicalRequestPlacement
{
    /// <summary>Gets this receiving native silo's current generation address.</summary>
    global::Orleans.Runtime.SiloAddress Current { get; }
    /// <summary>Gets the configured local native physical owner, independently checked against persisted SCAT.</summary>
    PhysicalShardRecord Owner { get; }
}
