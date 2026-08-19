using System.Reflection;
using System.Reflection.Emit;
using SemanticRelationships.Consumers;
using SemanticRelationships.Contracts;
using SemanticRelationships.Implementations;

var renderMethods = new Dictionary<Type, MethodInfo>();
var formatterTypes = new[]
{
    typeof(ILedgerFormatter),
    typeof(LedgerFormatter),
    typeof(WorkerLedgerFormatter),
};
foreach (var formatterType in formatterTypes)
{
    var render = formatterType.GetMethod(
        "Render",
        BindingFlags.Instance | BindingFlags.Public,
        binder: null,
        types: [typeof(string)],
        modifiers: null);
    var format = formatterType.GetMethod(
        "Format",
        BindingFlags.Instance | BindingFlags.Public,
        binder: null,
        types: [typeof(string)],
        modifiers: null);
    if (render is null || render.ReturnType != typeof(string) || format is not null)
    {
        return Reject(
            $"{formatterType.FullName} must expose Render(string) and no Format(string).");
    }

    renderMethods.Add(formatterType, render);
}

var ledgerCreate = typeof(LedgerReport).GetMethod(
    nameof(LedgerReport.Create),
    BindingFlags.Instance | BindingFlags.Public);
if (ledgerCreate is null || !ReturnsMethodResult(
        ledgerCreate,
        renderMethods[typeof(ILedgerFormatter)]))
{
    return Reject("LedgerReport.Create must call ILedgerFormatter.Render.");
}

var workerRun = typeof(WorkerJob).GetMethod(
    nameof(WorkerJob.Run),
    BindingFlags.Instance | BindingFlags.Public);
if (workerRun is null || !ReturnsMethodResult(
        workerRun,
        renderMethods[typeof(WorkerLedgerFormatter)]))
{
    return Reject("WorkerJob.Run must call WorkerLedgerFormatter.Render.");
}

const string sentinel = "semantic-oracle-sentinel";
var ledgerProxy = DispatchProxy.Create<
    ILedgerFormatter,
    RecordingFormatterProxy>();
var ledgerProxyState = (RecordingFormatterProxy)(object)ledgerProxy;
ledgerProxyState.ReturnValue = sentinel;
var sentinelReport = new LedgerReport(ledgerProxy);
if (sentinelReport.Create("entry") != sentinel ||
    ledgerProxyState.InvocationCount != 1 ||
    ledgerProxyState.LastMethodName != "Render")
{
    return Reject(
        "LedgerReport.Create must return the value from one Render invocation.");
}

var ledgerFormatter = new LedgerFormatter();
var workerFormatter = new WorkerLedgerFormatter();
var ledgerReport = new LedgerReport(ledgerFormatter);
var workerJob = new WorkerJob(workerFormatter);
if (ledgerReport.Create("entry") != "ledger:entry")
{
    return Reject("The interface-typed ledger call path changed behavior.");
}

if (workerJob.Run("entry") != "worker:entry")
{
    return Reject("The concrete worker call path changed behavior.");
}

Console.WriteLine("semantic-oracle: verified");
return 0;

static bool ReturnsMethodResult(MethodInfo caller, MethodInfo expectedCallee)
{
    var body = caller.GetMethodBody();
    var il = body?.GetILAsByteArray();
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
            ? position < il.Length
                ? twoByteOpCodes[il[position++]]
                : default
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
                if (called is not null &&
                    called.Module == expectedCallee.Module &&
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
        OperandType.ShortInlineBrTarget or
        OperandType.ShortInlineI or
        OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or
        OperandType.InlineField or
        OperandType.InlineI or
        OperandType.InlineMethod or
        OperandType.InlineSig or
        OperandType.InlineString or
        OperandType.InlineTok or
        OperandType.InlineType or
        OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or
        OperandType.InlineR => 8,
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

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        InvocationCount++;
        LastMethodName = targetMethod?.Name;
        return ReturnValue;
    }
}
