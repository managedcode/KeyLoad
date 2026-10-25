using System.Collections.Immutable;

namespace KeyLoad.Core;

/// <summary>Owns authorized immutable inputs and the original budget after the canonical read gate has returned.</summary>
public sealed class AnalyticsAggregateSnapshot
{
    internal AnalyticsAggregateSnapshot(AggregateAnalyticsRequest request, PrincipalRecord principal,
        ResourceDefinition resource, ResourceLifetime lifetime, ImmutableArray<AnalyticsEventRecord> rows,
        Guid incarnation, long readGeneration, long generation, long position, ReadExecutionBudget work, TimeProvider clock)
    {
        Request = request;
        Principal = principal;
        Resource = resource;
        Lifetime = lifetime;
        Rows = rows;
        SourceIncarnation = incarnation;
        SourceReadGeneration = readGeneration;
        SourceGeneration = generation;
        SourcePosition = position;
        Work = work;
        Clock = clock;
    }
    /// <summary>Gets the exact original aggregate request.</summary>
    public AggregateAnalyticsRequest Request
    {
        get;
    }
    /// <summary>Gets the immutable declared schema captured with these rows.</summary>
    public AnalyticsStoreSchema Schema => Resource.AnalyticsSchema!;
    /// <summary>Gets authorized owned rows from the complete bounded source scan.</summary>
    public ImmutableArray<AnalyticsEventRecord> Rows
    {
        get;
    }
    /// <summary>Gets the canonical source incarnation.</summary>
    public Guid SourceIncarnation
    {
        get;
    }
    /// <summary>Gets the canonical tree read generation.</summary>
    public long SourceReadGeneration
    {
        get;
    }
    /// <summary>Gets the scoped event mutation generation.</summary>
    public long SourceGeneration
    {
        get;
    }
    /// <summary>Gets the observed canonical position, without requiring unrelated writes to stop.</summary>
    public long SourcePosition
    {
        get;
    }
    internal PrincipalRecord Principal
    {
        get;
    }
    internal ResourceDefinition Resource
    {
        get;
    }
    internal ResourceLifetime Lifetime
    {
        get;
    }
    internal ReadExecutionBudget Work
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
}
