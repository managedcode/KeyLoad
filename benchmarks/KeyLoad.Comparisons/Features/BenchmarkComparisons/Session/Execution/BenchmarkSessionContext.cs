using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionContext
{
    internal BenchmarkSessionContext(IComparisonTarget target, IOptions<NativeComparisonExecutionOptions> executionOptions,
        BenchmarkSessionOptions options, bool development, TimeProvider? provider, Action<DocumentComparisonProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(development);
        Target = target;
        Options = options;
        ExecutionOptions = NativeComparisonExecutionOptions.Require(executionOptions);
        Clock = provider ?? TimeProvider.System;
        Progress = progress;
        Corpus = new(options.Records, options.Concurrency);
        State = new(options);
        Measurement = new(this);
        Verification = new(this);
    }
    internal IComparisonTarget Target
    {
        get;
    }
    internal BenchmarkSessionOptions Options
    {
        get;
    }
    internal IOptions<NativeComparisonExecutionOptions> ExecutionOptions
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    internal Action<DocumentComparisonProgress>? Progress
    {
        get;
    }
    internal DocumentComparisonCorpus Corpus
    {
        get;
    }
    internal BenchmarkSessionState State
    {
        get;
    }
    internal BenchmarkSessionMeasurement Measurement
    {
        get;
    }
    internal BenchmarkSessionDocumentVerification Verification
    {
        get;
    }
    internal int StableIdentity(int ordinal, bool random)
    {
        var count = Options.Records - BenchmarkSessionLimits.StableMutationGroups * Options.Operations;
        return BenchmarkSessionLimits.StableMutationGroups * Options.Operations + (random ? (int)(((long)ordinal * BenchmarkSessionLimits.ReadStride + BenchmarkSessionLimits.CorpusSeed) % count) : ordinal % count);
    }
}
