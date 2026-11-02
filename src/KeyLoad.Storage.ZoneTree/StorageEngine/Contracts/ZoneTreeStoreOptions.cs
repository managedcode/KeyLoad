using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Configures one node-local ZoneTree store and its read budgets.</summary>
/// <param name="Directory">Private directory that owns the identity and the ZoneTree directory.</param>
public sealed record ZoneTreeStoreOptions(string Directory)
{
    private int? maximumRangeRecords;
    private long? maximumRangeWorkBytes;
    private int? maximumReadCutRecords;
    private long? maximumReadCutExaminedBytes;
    private TimeSpan? maximumReadCutElapsed;
    private int? identityBufferBytes;
    private int? streamBufferBytes;
    private int? maximumIdentityFileBytes;
    /// <summary>Optional stable node incarnation required when reopening an existing store.</summary>
    public Guid? Incarnation
    {
        get; init;
    }
    /// <summary>Optional private signing key; caller bytes are copied before becoming store identity.</summary>
    public ReadOnlyMemory<byte>? SigningKey
    {
        get; init;
    }
    /// <summary>Optional observer invoked at named durable state transitions.</summary>
    public Action<CommitStage, long, int>? FaultObserver
    {
        get; init;
    }
    internal int MaximumRangeRecords => maximumRangeRecords ?? throw UnresolvedExecution();
    internal long MaximumRangeWorkBytes => maximumRangeWorkBytes ?? throw UnresolvedExecution();
    internal int MaximumReadCutRecords => maximumReadCutRecords ?? throw UnresolvedExecution();
    internal long MaximumReadCutExaminedBytes => maximumReadCutExaminedBytes ?? throw UnresolvedExecution();
    internal TimeSpan MaximumReadCutElapsed => maximumReadCutElapsed ?? throw UnresolvedExecution();
    internal int IdentityBufferBytes => identityBufferBytes ?? throw UnresolvedExecution();
    internal int StreamBufferBytes => streamBufferBytes ?? throw UnresolvedExecution();
    internal int MaximumIdentityFileBytes => maximumIdentityFileBytes ?? throw UnresolvedExecution();
    private static InvalidOperationException UnresolvedExecution() => new(ExecutionOptionsRequired);
    private const string ExecutionOptionsRequired = "Store execution policy must be resolved before opening execution storage.";

    /// <summary>Freezes centrally configured policy for this store.</summary>
    /// <param name="options">The centrally validated execution storage execution options.</param>
    /// <returns>A descriptor whose public scalar budgets are the exact effective values used by storage and offline bindings.</returns>
    public ZoneTreeStoreOptions ResolveExecutionOptions(IOptions<ZoneTreeStorageExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options IOptions reference through its object parameter. */;
        var configured = options.Value;
        ArgumentNullException.ThrowIfNull(configured) /* KLD0041: BCL null validation accepts the configured ZoneTreeStorageExecutionOptions reference through its object parameter. */;
        configured.Validate();
        var resolved = WithExecutionSnapshot(configured);
        return resolved;
    }

    internal ZoneTreeStoreOptions WithExecutionSnapshot(ZoneTreeStorageExecutionOptions configured)
    {
        configured.Validate();
        var effective = configured;
        var resolved = this with
        {
        };
        resolved.maximumRangeRecords = effective.MaximumRangeRecords;
        resolved.maximumRangeWorkBytes = effective.MaximumRangeWorkBytes;
        resolved.maximumReadCutRecords = effective.MaximumReadCutRecords;
        resolved.maximumReadCutExaminedBytes = effective.MaximumReadCutExaminedBytes;
        resolved.maximumReadCutElapsed = effective.MaximumReadCutElapsed;
        resolved.identityBufferBytes = effective.IdentityBufferBytes;
        resolved.streamBufferBytes = effective.StreamBufferBytes;
        resolved.maximumIdentityFileBytes = effective.MaximumIdentityFileBytes;
        return resolved;
    }
}
