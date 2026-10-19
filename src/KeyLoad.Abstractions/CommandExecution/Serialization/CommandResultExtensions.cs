using ManagedCode.Communication;

namespace KeyLoad;

/// <summary>Extracts the exact database result type from the execution communication result.</summary>
public static class CommandResultExtensions
{
    private const string WrongResultType = "The database command result type is invalid.";

    /// <summary>Returns the typed successful value or raises the safe domain failure.</summary>
    /// <typeparam name="T">The exact expected business result type.</typeparam>
    /// <param name="result">The original execution command result.</param>
    /// <returns>The successful business value.</returns>
    public static T Get<T>(this Result<object> result)
    {
        if (result.IsFailed)
        {
            var problem = result.Problem!;
            throw Errors.Fail(Errors.Code(problem)!.Value, problem.Detail!);
        }
        if (result.Value is T value)
        {
            return value;
        }
        throw Errors.Fail(ErrorCode.Corruption, WrongResultType);
    }
}
