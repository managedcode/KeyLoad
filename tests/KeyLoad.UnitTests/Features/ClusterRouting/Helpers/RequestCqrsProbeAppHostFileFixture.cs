using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeAppHostFileFixture : IDisposable
{
    private const string RootPrefix = "keyload-request-probe-apphost-";
    private const string OwnerFile = "owner.json";
    private const string Session = "8d884d08cb4d4c508fa4f23d8f61f04a";
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly string[] Voters = ["node1", "node2", "node3"];
    private bool parentOwned;

    private RequestCqrsProbeAppHostFileFixture(string parent)
    {
        Parent = parent;
        Root = Path.Combine(parent, "control");
        DataRoot = Path.Combine(parent, "data");
    }

    private void Initialize()
    {
        if (Directory.Exists(Parent) || File.Exists(Parent))
        { throw new IOException("The private AppHost fixture root already exists."); }
        Directory.CreateDirectory(Parent);
        parentOwned = true;
        File.SetUnixFileMode(Parent, PrivateDirectory);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataRoot);
        File.SetUnixFileMode(Root, PrivateDirectory);
        File.SetUnixFileMode(DataRoot, PrivateDirectory);
        foreach (var voter in Voters)
        {
            var node = Path.Combine(Root, voter);
            Directory.CreateDirectory(node);
            File.SetUnixFileMode(node, PrivateDirectory);
            WriteOwner(voter, Session, ClusterResources.Origin(voter));
        }
    }

    internal string Parent { get; }
    internal string Root { get; }
    internal string DataRoot { get; }
    internal string SessionId => Session;

    private static RequestCqrsProbeAppHostFileFixture Create()
        => new(Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N")));

    internal string Node(string voter) => Path.Combine(Root, voter);
    internal string Owner(string voter) => Path.Combine(Node(voter), OwnerFile);

    internal void WriteOwner(string voter, string? session = null, string? ownerVoter = null, int bytes = 0)
    {
        var path = Owner(voter);
        var text = OwnerJson(session ?? Session, ownerVoter ?? ClusterResources.Origin(voter));
        var content = Encoding.UTF8.GetBytes(text);
        if (bytes > content.Length)
        {
            Array.Resize(ref content, bytes);
            content.AsSpan(text.Length).Fill((byte)' ');
        }
        File.WriteAllBytes(path, content);
        File.SetUnixFileMode(path, PrivateFile);
    }

    public void Dispose()
    {
        if (parentOwned)
        {
            Directory.Delete(Parent, recursive: true);
            parentOwned = false;
        }
    }

    internal static async Task WithFixtureAsync(Func<RequestCqrsProbeAppHostFileFixture, Task> action)
    {
        var failures = new List<Exception>();
        RequestCqrsProbeAppHostFileFixture? fixture = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            fixture = Create();
            fixture.Initialize();
            return action(fixture);
        }, failures);
        if (fixture is not null)
        { ServerFailureObserver.Observe(fixture.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string OwnerJson(string session, string voter)
        => "{\"Version\":1,\"Kind\":\"Owner\",\"SessionId\":\"" + session
            + "\",\"Voter\":\"" + voter + "\"}";
}
