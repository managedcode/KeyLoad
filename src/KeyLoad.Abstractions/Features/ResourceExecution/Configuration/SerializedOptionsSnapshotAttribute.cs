namespace KeyLoad;

/// <summary>Identifies an immutable record's serialized option metadata, separate from execution injection.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SerializedOptionsSnapshotAttribute : Attribute;
