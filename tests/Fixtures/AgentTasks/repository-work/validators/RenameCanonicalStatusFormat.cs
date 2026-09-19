using System.Reflection;
using CanonicalContract = SemanticUnrelatedNames.Canonical.Contracts.IStatusFormatter;
using CanonicalFormatter = SemanticUnrelatedNames.Canonical.Implementations.StatusFormatter;
using CanonicalView = SemanticUnrelatedNames.Canonical.Consumers.StatusView;
using ArchiveContract = SemanticUnrelatedNames.Archive.IStatusFormatter;
using ArchiveFormatter = SemanticUnrelatedNames.Archive.StatusFormatter;
using ArchiveView = SemanticUnrelatedNames.Archive.StatusView;

var stringParameters = new[] { typeof(string) };
foreach (var type in new[] { typeof(CanonicalContract), typeof(CanonicalFormatter) })
{
    if (type.GetMethod("Render", stringParameters)?.ReturnType != typeof(string) ||
        type.GetMethod("Format", stringParameters) is not null)
    {
        return Reject("The canonical contract and implementation must expose only Render(string).");
    }
}

foreach (var type in new[] { typeof(ArchiveContract), typeof(ArchiveFormatter) })
{
    if (type.GetMethod("Format", stringParameters)?.ReturnType != typeof(string) ||
        type.GetMethod("Render", stringParameters) is not null)
    {
        return Reject("The unrelated archive declarations must retain only Format(string).");
    }
}

const string sentinel = "unrelated-name-oracle-sentinel";
var proxy = DispatchProxy.Create<CanonicalContract, RecordingProxy>();
var state = (RecordingProxy)(object)proxy;
state.ReturnValue = sentinel;
if (new CanonicalView(proxy).Create("entry") != sentinel ||
    state.InvocationCount != 1 || state.LastMethod != "Render")
{
    return Reject("The canonical call site must return the exact Render(string) result.");
}

if (new CanonicalView(new CanonicalFormatter()).Create("entry") != "canonical:entry" ||
    new ArchiveView(new ArchiveFormatter()).Create("entry") != "archive:entry")
{
    return Reject("Canonical or unrelated behavior changed.");
}

Console.WriteLine("semantic-oracle: verified");
return 0;

static int Reject(string message) { Console.Error.WriteLine(message); Console.WriteLine("semantic-oracle: rejected"); return 1; }

public class RecordingProxy : DispatchProxy
{
    public string ReturnValue { get; set; } = string.Empty;
    public int InvocationCount { get; private set; }
    public string? LastMethod { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        InvocationCount++;
        LastMethod = targetMethod?.Name;
        return ReturnValue;
    }
}
