using ManagedCode.Communication.Constants;
using StreamProblem = ManagedCode.Communication.Problem;

namespace KeyLoad.Clustering;

internal static class GrainRequestStreamProblem
{
    private const string ErrorTypePrefix = "urn:keyload:error:";
    internal static StreamProblem Create(ErrorCode code, string detail)
    {
        var name = code.ToString();
        var problem = new StreamProblem
        {
            Type = ErrorTypePrefix + name,
            Title = name,
            ErrorCode = name,
            StatusCode = Errors.Status(code),
            Detail = detail
        };
        Validate(problem);
        return problem;
    }

    internal static void Validate(StreamProblem problem) => ReadCode(problem);

    internal static ErrorCode ReadCode(StreamProblem problem)
    {
        const int ExtensionsFirstCount = 1;
        const int DetailEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(problem) /* KLD0041: BCL null validation accepts the problem Problem reference through its object parameter. */;
        if ((problem.Extensions /* KLD0041: Validate the producer-defined object-valued Problem.Extensions entry as an exact canonical string without broadening accepted error-code types. */) is not { Count: ExtensionsFirstCount }
            || !((problem.Extensions /* KLD0041: Validate the producer-defined object-valued Problem.Extensions entry as an exact canonical string without broadening accepted error-code types. */).TryGetValue(ProblemConstants.ExtensionKeys.ErrorCode, out var field) /* KLD0041: Validate the producer-defined object-valued Problem.Extensions entry as an exact canonical string without broadening accepted error-code types. */)
            || (field is not string name /* KLD0042: The producer Problem.Extensions dictionary is object-valued; require its exact string error code without the producer getter converting arbitrary values with ToString. */) || !Enum.TryParse<ErrorCode>(name, out var code)
            || !Enum.IsDefined(code) || !string.Equals(code.ToString(), name, StringComparison.Ordinal)
            || problem.Type != ErrorTypePrefix + name || problem.Title != name
            || problem.StatusCode != Errors.Status(code) || problem.Instance is not null
            || problem.Detail is not { Length: > DetailEmptyCount })
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        return code;
    }
}
