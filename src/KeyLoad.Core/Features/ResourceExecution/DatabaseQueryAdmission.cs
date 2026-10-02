using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private readonly AnalyticalReadGate analyticalReadGate = new((limits ?? new DatabaseLimits()).MaxConcurrentQueries);

    /// <summary>Gets the number of currently admitted analytical operations on this engine.</summary>
    public int QueryReadsInFlight => analyticalReadGate.InFlight;

    /// <summary>Reserves one shared analytical-read allowance until the returned lease is disposed.</summary>
    /// <param name="cancellationToken">Caller cancellation checked before and after reservation.</param>
    /// <returns>An owned, idempotent reservation.</returns>
    public IDisposable AdmitQuery(CancellationToken cancellationToken) => analyticalReadGate.Admit(cancellationToken);
}
