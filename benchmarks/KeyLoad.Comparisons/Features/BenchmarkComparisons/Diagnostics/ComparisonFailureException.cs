namespace KeyLoad.Comparisons;

/// <summary>Represents a failure identified by a harness error code.</summary>
public sealed class ComparisonFailureException : Exception
{
    /// <summary>Initializes a comparison failure with the standard exception message.</summary>
    public ComparisonFailureException()
    {
    }

    /// <summary>Initializes a comparison failure with the supplied message.</summary>
    /// <param name="message">The coded comparison failure message.</param>
    public ComparisonFailureException(string? message) : base(message)
    {
    }

    /// <summary>Initializes a comparison failure with the supplied message and cause.</summary>
    /// <param name="message">The coded comparison failure message.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public ComparisonFailureException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
