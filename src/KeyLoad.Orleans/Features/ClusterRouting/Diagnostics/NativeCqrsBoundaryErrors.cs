using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>Separates fatal runtime failures from failures which may be returned through the bounded stream.</summary>
internal static class NativeCqrsBoundaryErrors
{
    /// <summary>Returns whether the exception is nonfatal and can be safely classified at the request boundary.</summary>
    /// <param name="error">The observed exception.</param>
    /// <returns>False only for exceptions which must escape the producer handler.</returns>
    internal static bool IsNonFatal(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return CqrsRuntimeFailures.FindFatal(error) is null;
    }
}
