namespace KeyLoad.Core;

/// <summary>Provides shared caller-visible detail for command outcome ownership.</summary>
internal static class CommandAdmissionDetails
{
    /// <summary>Describes a command whose result is unknown after its consumer stops owning it.</summary>
    public const string UnknownWriteOutcome = "The node stopped before returning a command outcome. Query or retry the same command ID.";
}
