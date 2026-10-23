namespace KeyLoad.UnitTests;

/// <summary>Invalid request envelope scope cases.</summary>
internal enum GrainRequestMutation
{
    /// <summary>A different protocol purpose.</summary>
    Purpose,
    /// <summary>A different installation.</summary>
    Incarnation,
    /// <summary>An expired request.</summary>
    Expired,
    /// <summary>An excessive future expiry.</summary>
    Future,
    /// <summary>Both read and command kinds.</summary>
    BothKinds,
    /// <summary>No operation kind.</summary>
    NoKind,
    /// <summary>A command ID on a read.</summary>
    CommandId,
    /// <summary>An undefined operation kind.</summary>
    UnknownKind
}
