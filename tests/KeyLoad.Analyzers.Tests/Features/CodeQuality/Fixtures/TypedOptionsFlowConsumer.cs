using Microsoft.Extensions.Options;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal sealed class TypedOptionsFlowConsumer
{
    private readonly TypedOptionsFlowSettings snapshot;
    private readonly TypedOptionsFlowState state;

    public TypedOptionsFlowConsumer(IOptions<TypedOptionsFlowSettings> options, TypedOptionsFlowState state)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);
        snapshot = options.Value;
        this.state = state;
    }

    internal async Task<int> ExecuteAsync(int requestedResult)
    {
        using var admission = new SemaphoreSlim(snapshot.AdmissionCapacity, snapshot.AdmissionCapacity);
        state.InitialPermits = admission.CurrentCount;
        state.ObservedDeadline = snapshot.Deadline;
        await admission.WaitAsync();
        try
        {
            var result = await Task.FromResult(requestedResult).WaitAsync(snapshot.Deadline);
            state.AdmittedOperations++;
            return result;
        }
        finally
        {
            admission.Release();
        }
    }
}
