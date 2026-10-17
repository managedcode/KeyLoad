namespace KeyLoad;

/// <summary>Selects whether a field patch sets or removes a value.</summary>
public enum PatchKind
{
    /// <summary>Set the field to the supplied JSON value.</summary>
    Set,
    /// <summary>Remove the field from the document.</summary>
    Remove
}
