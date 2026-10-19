namespace KeyLoad.Features.InternalSerialization;

// Structural safety fences retain the existing JSON depth and the configured HTTP body ceiling.
// They do not replace operation-specific admission, accounting or decoded heap qualification.
internal static class SerializationLimits
{
    internal const int SemanticDepth = 64;
    internal const int WrapperFactor = 4;
    internal const int EnvelopeAllowance = 8;
    internal const int WireDepth = SemanticDepth * WrapperFactor + EnvelopeAllowance;
    internal const int MaximumDomBytes = 33_554_432;
}
