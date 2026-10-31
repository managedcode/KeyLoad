namespace KeyLoad;

/// <summary>Contains database input and original facts prepared before ordered atomic application.</summary>
/// <param name="Kind">The typed database operation.</param>
/// <param name="Input">The typed business input of that operation.</param>
/// <param name="Fingerprint">The immutable fingerprint of the original public input and command identity.</param>
/// <param name="RetryDecisions">Original bounded queue retry choices, when required.</param>
/// <param name="ResourceAdmission">Original resource lifetimes captured for the operation.</param>
[Orleans.GenerateSerializer]
public sealed record DatabaseCommand(
    [property: Orleans.Id(DatabaseCommandFields.Kind)] OperationKind Kind,
    [property: Orleans.Id(DatabaseCommandFields.Input), KeyLoad.InternalSerialization.PublicInput] object Input,
    [property: Orleans.Id(DatabaseCommandFields.Fingerprint)] string Fingerprint,
    [property: Orleans.Id(DatabaseCommandFields.RetryDecisions)] ReadOnlyMemory<byte> RetryDecisions = default,
    [property: Orleans.Id(DatabaseCommandFields.ResourceAdmission)] ReadOnlyMemory<byte> ResourceAdmission = default);
