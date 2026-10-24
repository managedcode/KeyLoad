namespace KeyLoad.Storage;

/// <summary>Lets several independent actions share one atomic commit and its single durable journal flush.</summary>
/// <remarks>Each action sees every write staged before it. Its <see cref="IAtomicTransaction.Reset"/>,
/// <see cref="IAtomicTransaction.ValidateCommit"/>, candidate validation and frame budget cover only the writes the
/// action staged itself, so an action behaves as it would in a commit of its own.</remarks>
public interface IAtomicActionStaging
{
    /// <summary>Completes the current action, keeping its writes, and starts the next one.</summary>
    void BeginAction();

    /// <summary>Gets whether every staged write of all actions still fits one journal frame.</summary>
    bool FitsOneFrame
    {
        get;
    }
}
