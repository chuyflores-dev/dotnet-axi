using System.Reflection;
using Contract = SemanticInterfaceDispatch.Contracts.IMessageFormatter;
using EmailFormatter = SemanticInterfaceDispatch.Implementations.EmailMessageFormatter;
using SmsFormatter = SemanticInterfaceDispatch.Implementations.SmsMessageFormatter;
using PushFormatter = SemanticInterfaceDispatch.Implementations.PushMessageFormatter;
using Dispatcher = SemanticInterfaceDispatch.Consumers.MessageDispatcher;

var stringParameters = new[] { typeof(string) };
foreach (var type in new[] { typeof(Contract), typeof(EmailFormatter), typeof(SmsFormatter), typeof(PushFormatter) })
{
    if (type.GetMethod("Render", stringParameters)?.ReturnType != typeof(string) ||
        HasFormatStringMember(type))
    {
        return Reject("The contract and every implementation must expose only Render(string).");
    }
}

const string sentinel = "interface-dispatch-oracle-sentinel";
var proxy = DispatchProxy.Create<Contract, RecordingProxy>();
var state = (RecordingProxy)(object)proxy;
state.ReturnValue = sentinel;
var dispatcher = new Dispatcher(proxy, proxy, proxy);
if (dispatcher.DispatchEmail("entry") != sentinel ||
    dispatcher.DispatchSms("entry") != sentinel ||
    dispatcher.DispatchPush("entry") != sentinel ||
    state.InvocationCount != 3 ||
    state.MethodNames.Any(static name => name != "Render"))
{
    return Reject("Every interface-typed dispatch site must return the exact Render(string) result.");
}

var actual = new Dispatcher(new EmailFormatter(), new SmsFormatter(), new PushFormatter());
if (actual.DispatchEmail("entry") != "email:entry" ||
    actual.DispatchSms("entry") != "sms:entry" ||
    actual.DispatchPush("entry") != "push:entry")
{
    return Reject("Implementation behavior changed.");
}

Console.WriteLine("semantic-oracle: verified");
return 0;

static int Reject(string message) { Console.Error.WriteLine(message); Console.WriteLine("semantic-oracle: rejected"); return 1; }

static bool HasFormatStringMember(Type type)
    => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Any(static method =>
            (method.Name == "Format" || method.Name.EndsWith(".Format", StringComparison.Ordinal)) &&
            method.ReturnType == typeof(string) &&
            method.GetParameters() is [{ ParameterType: var parameterType }] &&
            parameterType == typeof(string));

public class RecordingProxy : DispatchProxy
{
    public string ReturnValue { get; set; } = string.Empty;
    public int InvocationCount { get; private set; }
    public List<string?> MethodNames { get; } = [];
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        InvocationCount++;
        MethodNames.Add(targetMethod?.Name);
        return ReturnValue;
    }
}
