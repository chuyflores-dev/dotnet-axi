using System.Reflection;
using System.Reflection.Emit;
using SemanticOverloadRelationships.Consumers;
using SemanticOverloadRelationships.Contracts;
using SemanticOverloadRelationships.Implementations;

var stringParameters = new[] { typeof(string) };
var integerParameters = new[] { typeof(int) };
var renderMethods = new Dictionary<Type, MethodInfo>();
var formatterTypes = new[]
{
    typeof(IReportFormatter),
    typeof(ReportFormatter),
    typeof(WorkerReportFormatter),
};
foreach (var formatterType in formatterTypes)
{
    var render = formatterType.GetMethod("Render", stringParameters);
    var retainedStringFormat = formatterType.GetMethod("Format", stringParameters);
    var integerFormat = formatterType.GetMethod("Format", integerParameters);
    var renamedIntegerFormat = formatterType.GetMethod("Render", integerParameters);
    if (render is null || render.ReturnType != typeof(string) ||
        retainedStringFormat is not null || integerFormat?.ReturnType != typeof(string) ||
        renamedIntegerFormat is not null)
    {
        return Reject(
            $"{formatterType.FullName} must expose Render(string), retain Format(int), and expose no other overload rename.");
    }

    renderMethods.Add(formatterType, render);
}

var reportRender = typeof(ReportView).GetMethod(
    nameof(ReportView.Render),
    BindingFlags.Instance | BindingFlags.Public);
var workerRender = typeof(WorkerReportView).GetMethod(
    nameof(WorkerReportView.Render),
    BindingFlags.Instance | BindingFlags.Public);
if (reportRender is null || !ReturnsMethodResult(
        reportRender,
        renderMethods[typeof(IReportFormatter)]) ||
    workerRender is null || !ReturnsMethodResult(
        workerRender,
        renderMethods[typeof(WorkerReportFormatter)]))
{
    return Reject("String call sites must invoke the exact Render(string) overload.");
}

const string sentinel = "semantic-overload-oracle-sentinel";
var proxy = DispatchProxy.Create<IReportFormatter, RecordingFormatterProxy>();
var proxyState = (RecordingFormatterProxy)(object)proxy;
proxyState.ReturnValue = sentinel;
if (new ReportView(proxy).Render("entry") != sentinel ||
    proxyState.InvocationCount != 1 || proxyState.LastMethodName != "Render")
{
    return Reject("ReportView.Render must return one IReportFormatter.Render invocation.");
}

var reportFormatter = new ReportFormatter();
var workerFormatter = new WorkerReportFormatter();
var reportView = new ReportView(reportFormatter);
var workerReportView = new WorkerReportView(workerFormatter);
if (reportView.Render("entry") != "report:entry" ||
    workerReportView.Render("entry") != "worker:entry" ||
    reportView.Revision(42) != "report-number:42" ||
    workerReportView.Revision(42) != "worker-number:42")
{
    return Reject("String or integer formatting behavior changed.");
}

Console.WriteLine("semantic-oracle: verified");
return 0;

static bool ReturnsMethodResult(MethodInfo caller, MethodInfo expectedCallee)
{
    var il = caller.GetMethodBody()?.GetILAsByteArray();
    if (il is null)
    {
        return false;
    }

    var oneByteOpCodes = new OpCode[0x100];
    var twoByteOpCodes = new OpCode[0x100];
    foreach (var field in typeof(OpCodes).GetFields(
                 BindingFlags.Public | BindingFlags.Static))
    {
        if (field.GetValue(null) is not OpCode opCode)
        {
            continue;
        }

        var value = unchecked((ushort)opCode.Value);
        if (value < 0x100)
        {
            oneByteOpCodes[value] = opCode;
        }
        else if ((value & 0xff00) == 0xfe00)
        {
            twoByteOpCodes[value & 0xff] = opCode;
        }
    }

    var position = 0;
    while (position < il.Length)
    {
        var first = il[position++];
        var opCode = first == 0xfe
            ? position < il.Length ? twoByteOpCodes[il[position++]] : default
            : oneByteOpCodes[first];
        if (opCode.Size == 0)
        {
            return false;
        }

        var operandSize = GetOperandSize(opCode.OperandType, il, position);
        if (operandSize < 0 || position + operandSize > il.Length)
        {
            return false;
        }

        if ((opCode == OpCodes.Call || opCode == OpCodes.Callvirt) &&
            operandSize == sizeof(int))
        {
            var token = BitConverter.ToInt32(il, position);
            try
            {
                var called = caller.Module.ResolveMethod(
                    token,
                    caller.DeclaringType?.GetGenericArguments(),
                    caller.GetGenericArguments());
                if (called is not null && called.Module == expectedCallee.Module &&
                    called.MetadataToken == expectedCallee.MetadataToken)
                {
                    position += operandSize;
                    while (position < il.Length &&
                           oneByteOpCodes[il[position]] == OpCodes.Nop)
                    {
                        position++;
                    }

                    return position < il.Length &&
                        oneByteOpCodes[il[position]] == OpCodes.Ret &&
                        position + 1 == il.Length;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        position += operandSize;
    }

    return false;
}

static int GetOperandSize(OperandType operandType, byte[] il, int position) =>
    operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
            OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or
            OperandType.InlineI or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or
            OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch when position + sizeof(int) <= il.Length =>
            sizeof(int) + (sizeof(int) * BitConverter.ToInt32(il, position)),
        _ => -1,
    };

static int Reject(string message)
{
    Console.Error.WriteLine(message);
    Console.WriteLine("semantic-oracle: rejected");
    return 1;
}

public class RecordingFormatterProxy : DispatchProxy
{
    public int InvocationCount { get; private set; }

    public string? LastMethodName { get; private set; }

    public string ReturnValue { get; set; } = string.Empty;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        InvocationCount++;
        LastMethodName = targetMethod?.Name;
        return ReturnValue;
    }
}
