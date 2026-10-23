using System.Collections.Immutable;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution.Execution;

/// <summary>Retains observed execution pairs and deduplicated dependencies inside the original operation budget.</summary>
internal sealed class ReadExecutionBudgetLifetimeBindings(ReadExecutionBudget work)
{
    internal const string AlreadyAttached = "The read resource lifetime binding is already attached.";
    internal const string MissingBinding = "The read resource lifetime work owner is unavailable.";
    private readonly Dictionary<(PartitionRef Partition, string Name), ResourceLifetime> local = [];
    private IResourceLifetimeWorkBinding? binding;
    private ResourceLifetimeWorkResultRetention? resultRetention;

    internal int AvailableResultBytes(int maximumBytes)
        => resultRetention?.AvailableResultBytes(maximumBytes) ?? maximumBytes;

    internal void Attach(IResourceLifetimeWorkBinding value)
    {
        work.Check();
        ArgumentNullException.ThrowIfNull(value);
        if (binding is not null)
        {
            Require(value);
            return;
        }
        if (local.Count != 0)
        {
            throw new InvalidOperationException(AlreadyAttached);
        }
        resultRetention = value.ResultRetention;
        binding = value;
    }

    internal IResourceLifetimeWorkBinding RequireAttached()
    {
        var original = binding ?? throw new InvalidOperationException(MissingBinding);
        Require(original);
        return original;
    }

    internal void Require(IResourceLifetimeWorkBinding value)
    {
        work.Check();
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(binding, value) || !ReferenceEquals(resultRetention, value.ResultRetention))
        {
            throw new InvalidOperationException(MissingBinding);
        }
    }

    internal void Observe(PartitionRef partition, ResourceDefinition definition, ResourceLifetime lifetime)
    {
        work.Check();
        ObserveAuthenticated(partition, lifetime);
        local[(partition, definition.Name)] = lifetime;
    }

    internal void ObserveAuthenticated(PartitionRef partition, ResourceLifetime lifetime)
    {
        work.Check();
        var coverage = new ResourceLifetimeCoverage(partition, [lifetime]);
        resultRetention?.Reserve(coverage.Lifetimes, work.LifetimeResultBytesLimit);
        binding?.Bind(coverage);
        work.Check();
    }

    internal void ObserveAuthenticated(ImmutableArray<ResourceLifetime> lifetimes)
    {
        work.Check();
        resultRetention?.Reserve(lifetimes, work.LifetimeResultBytesLimit);
        binding?.Bind(lifetimes);
        work.Check();
    }

    internal ResourceLifetimeCoverage Capture(DatabaseEngine database, IKeyValueView authorizedView, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, bool requireBinding)
    {
        work.Check();
        ArgumentNullException.ThrowIfNull(authorizedView);
        if (requireBinding && binding is null)
        {
            throw new InvalidOperationException(MissingBinding);
        }
        var values = ImmutableArray.CreateBuilder<ResourceLifetime>(definitions.Length);
        foreach (var definition in definitions)
        {
            if (!local.TryGetValue((partition, definition.Name), out var lifetime))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority);
            }
            values.Add(lifetime);
        }
        var coverage = new ResourceLifetimeCoverage(partition, values.MoveToImmutable());
        ResourceLifetimeCoverageValidation.Require(coverage, partition, definitions, database.Limits.MaxBatchBytes);
        binding?.Bind(coverage);
        work.Check();
        return coverage;
    }

    internal ResourceLifetimeCoverage CaptureAuthenticated(DatabaseEngine database, IKeyValueView authorizedLocalView,
        ResourceLifetimeCoverage coverage, ImmutableArray<ResourceDefinition> definitions, bool requireBinding)
    {
        work.Check();
        if (requireBinding && binding is null)
        {
            throw new InvalidOperationException(MissingBinding);
        }
        if (coverage is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority);
        }
        ResourceLifetimeCoverageValidation.Require(coverage, coverage.Partition, definitions, database.Limits.MaxBatchBytes);
        var bindingView = new AuthenticatedResourceLifetimeReadView(authorizedLocalView, this);
        for (var index = 0; index < definitions.Length; index++)
        {
            ResourceLifetimeReadBinding.BindAuthenticatedReadResourceLifetime(bindingView, coverage.Partition,
                            definitions[index], coverage.Lifetimes[index]);
        }
        work.Check();
        return coverage;
    }

}
