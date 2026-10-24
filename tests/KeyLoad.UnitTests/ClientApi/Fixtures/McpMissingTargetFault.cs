namespace KeyLoad.UnitTests.ClientApi;

/// <summary>Names execution target omissions and concrete JSON type failures.</summary>
internal enum McpMissingTargetFault
{
    MissingParameters, NullParameters, ScalarParameters, ArrayParameters, MissingTarget, NullTarget, WrongTarget
}
