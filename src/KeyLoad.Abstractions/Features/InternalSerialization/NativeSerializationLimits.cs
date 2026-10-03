namespace KeyLoad.Features.InternalSerialization;

// Structural safety fences retain the existing JSON depth and the configured HTTP body ceiling.
// They do not replace operation-specific admission, accounting or decoded heap qualification.
internal static class NativeSerializationLimits
{
    internal const int SemanticDepth = 64;
    internal const int NativeWrapperFactor = 4;
    internal const int NativeEnvelopeAllowance = 8;
    internal const int WireDepth = SemanticDepth * NativeWrapperFactor + NativeEnvelopeAllowance;
    internal const int MaximumDomBytes = 33_554_432;
}
