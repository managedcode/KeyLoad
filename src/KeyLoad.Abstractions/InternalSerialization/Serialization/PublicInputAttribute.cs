namespace KeyLoad.InternalSerialization;

/// <summary>Marks a member holding a caller's public input, which keeps its public null elements for owner validation.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
internal sealed class PublicInputAttribute : Attribute;
