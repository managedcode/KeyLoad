namespace KeyLoad.Core;
/// <summary>Executes ResourceExecutionQueries operations against the caller's database context.</summary>
public static class ResourceExecutionQueries
{
    /// <summary>Counts one analytical read until the returned lease is disposed; it never refuses the read.</summary>
    /// <param name = "cancellationToken">Caller cancellation checked before and after reservation.</param>
    /// <returns>An owned, idempotent reservation.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static IDisposable AdmitQuery(this DatabaseEngine engine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.analyticalReadGate.Admit(cancellationToken);
    }
}
