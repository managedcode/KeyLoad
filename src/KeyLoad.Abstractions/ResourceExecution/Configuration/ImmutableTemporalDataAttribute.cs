namespace KeyLoad;

/// <summary>Identifies fixed temporal data in a static readonly TimeSpan field, never execution policy.</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class ImmutableTemporalDataAttribute : Attribute;
