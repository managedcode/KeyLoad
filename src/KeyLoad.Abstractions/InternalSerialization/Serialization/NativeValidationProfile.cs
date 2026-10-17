namespace KeyLoad.Features.InternalSerialization;

/// <summary>Preserves public JSON collection-element semantics until the owning domain validator runs.</summary>
internal enum NativeValidationProfile
{
    Strict,
    PublicInputElements
}
