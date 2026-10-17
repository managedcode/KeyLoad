namespace KeyLoad.Storage;

/// <summary>Represents an indexed key component which is absent rather than explicitly null.</summary>
public sealed class MissingValue
{
    private MissingValue()
    {
    }

    /// <summary>Gets the singleton missing-value marker.</summary>
    public static MissingValue Instance { get; } = new();
}
