using ManagedCode.Communication.Constants;
using StreamProblem = ManagedCode.Communication.Problem;

namespace KeyLoad.Orleans;

internal static class GrainRequestStreamProblem
{
    internal static StreamProblem Create(ErrorCode code, string detail)
    {
        var name = code.ToString();
        var problem = new StreamProblem
        {
            Type = $"urn:keyload:error:{name}",
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

        ArgumentNullException.ThrowIfNull(problem);
        if (problem.Extensions is not { Count: ExtensionsFirstCount }
            || !problem.Extensions.TryGetValue(ProblemConstants.ExtensionKeys.ErrorCode, out var field)
            || field is not string name || !Enum.TryParse<ErrorCode>(name, out var code)
            || !Enum.IsDefined(code) || !string.Equals(code.ToString(), name, StringComparison.Ordinal)
            || problem.Type != $"urn:keyload:error:{name}" || problem.Title != name
            || problem.StatusCode != Errors.Status(code) || problem.Instance is not null
            || problem.Detail is not { Length: > DetailEmptyCount and <= GrainRequestStreamProtocol.MaximumDetailCharacters })
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        return code;
    }
}
