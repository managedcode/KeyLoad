namespace KeyLoad;

internal static class DatabaseCommandFields
{
    internal const uint Kind = 0;
    // Field 1 was the encoded input. It is retired and must never be reused.
    // Field 2 was the input fingerprint. It is retired and must never be reused.
    internal const uint RetryDecisions = 3;
    internal const uint ResourceAdmission = 4;
    internal const uint Input = 5;
}
