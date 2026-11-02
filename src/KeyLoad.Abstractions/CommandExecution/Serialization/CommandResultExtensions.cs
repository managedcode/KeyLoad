using ManagedCode.Communication;

namespace KeyLoad;

/// <summary>Extracts the exact database result type from the execution communication result.</summary>
public static class CommandResultExtensions
{
    private const string WrongResultType = "The database command result type is invalid.";

    /// <summary>Returns the successful typed value or raises the safe domain failure.</summary>
    /// <typeparam name="T">The command's business result type.</typeparam>
    /// <param name="result">The typed execution command result.</param>
    /// <returns>The successful business value.</returns>
    public static T Get<T>(this Result<T> result)
    {
        if (result.IsFailed)
        {
            var problem = result.Problem;
            throw Errors.Fail(Errors.Code(problem).Value!.Value, problem.Detail!);
        }
        return result.Value;
    }

    /// <summary>Returns the typed successful value or raises the safe domain failure.</summary>
    /// <typeparam name="T">The exact expected business result type.</typeparam>
    /// <param name="result">The original execution command result.</param>
    /// <returns>The successful business value.</returns>
    public static T Get<T>(this Result<object> /* KLD0041: This overload extracts one exact business type from the existing persisted mixed-operation outcome format. */ result)
    {
        if (result.IsFailed)
        {
            var problem = result.Problem;
            throw Errors.Fail(Errors.Code(problem).Value!.Value, problem.Detail!);
        }
        if (result.Value /* KLD0041: The current stored-outcome payload is erased at its native serialization boundary. */ is T value /* KLD0042: A stored outcome must match the caller's exact expected business type before leaving this boundary. */)
        {
            return value;
        }
        throw Errors.Fail(ErrorCode.Corruption, WrongResultType);
    }
}
