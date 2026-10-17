namespace KeyLoad.Comparisons;

/// <summary>Defines the lifecycle and capabilities of a comparison database target.</summary>
public interface IComparisonTarget : IAsyncDisposable
{
    private const string OutsideThisTargetSSharedContractDetail = "Outside this target's shared contract.";

    /// <summary>Gets the target's descriptive profile.</summary>
    TargetProfile Profile
    {
        get;
    }
    /// <summary>Determines whether the target supports the specified workload scenario.</summary>
    /// <param name="scenario">The workload scenario to check.</param>
    /// <returns><see langword="true"/> when the target supports the scenario; otherwise, <see langword="false"/>.</returns>
    bool Supports(Scenario scenario);
    /// <summary>Gets the reason a scenario is outside the target's shared contract.</summary>
    string UnsupportedReason => OutsideThisTargetSSharedContractDetail;
    /// <summary>Initializes the target with the benchmark corpus.</summary>
    /// <param name="dataset">The deterministic corpus and workload oracle.</param>
    /// <param name="cancellationToken">A token used to cancel initialization.</param>
    /// <returns>A task that completes when initialization finishes.</returns>
    Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken);
    /// <summary>Opens a session for executing operations against the target.</summary>
    /// <param name="cancellationToken">A token used to cancel session creation.</param>
    /// <returns>A task whose result is the opened comparison session.</returns>
    Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken);
}
