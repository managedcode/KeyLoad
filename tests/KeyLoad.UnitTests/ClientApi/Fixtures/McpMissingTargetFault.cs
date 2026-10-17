namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Names native target omissions and concrete JSON type failures.</summary>
internal enum McpMissingTargetFault
{
    MissingParameters, NullParameters, ScalarParameters, ArrayParameters, MissingTarget, NullTarget, WrongTarget
}
