namespace KeyLoad.InternalSerialization;

/// <summary>Preserves public JSON collection-element semantics until the owning domain validator runs.</summary>
internal enum ValidationProfile
{
    Strict,
    PublicInputElements
}
