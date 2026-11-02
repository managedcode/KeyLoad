namespace KeyLoad.Storage;

/// <summary>Lets several independent actions share one process-persistent native atomic ZoneTree commit.</summary>
/// <remarks>Each action sees every write staged before it. Its <see cref="IAtomicTransaction.Reset"/> covers only the
/// writes the action staged itself, so an action behaves as it would in a commit of its own.</remarks>
public interface IAtomicActionStaging
{
    /// <summary>Completes the current action, keeping its writes, and starts the next one.</summary>
    void BeginAction();
}
