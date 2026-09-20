using System.Reflection;
var models=Assembly.Load("Models"); var consumers=Assembly.Load("Consumers"); var candidateTests=Assembly.Load("CandidateTests");
var formatter=models.GetType("SemanticImpact.Models.ImpactFormatter")!;
var render=formatter.GetMethod("Render",BindingFlags.Public|BindingFlags.Instance,[typeof(string)]);
var numericFormat=formatter.GetMethod("Format",BindingFlags.Public|BindingFlags.Instance,[typeof(int)]);
if(render?.ReturnType!=typeof(string)||numericFormat?.ReturnType!=typeof(string)||HasStringFormat(formatter)) return Reject("The impact string member or numeric overload changed incorrectly.");
var value=Activator.CreateInstance(formatter)!;
if(render.Invoke(value,["entry"]) as string!="impact:entry:1"||StringCallCount(value)!=1||numericFormat.Invoke(value,[7]) as string!="number:7") return Reject("Formatter behavior changed.");
if(!VerifyCaller(consumers,"SemanticImpact.Consumers.ImpactView","Create",formatter,render,numericFormat)||!VerifyCaller(candidateTests,"SemanticImpact.CandidateTests.ImpactFormatterTests","Verify",formatter,render,numericFormat)) return Reject("A production or candidate-test call site changed incorrectly.");
Console.WriteLine("semantic-oracle: verified"); return 0;
bool VerifyCaller(Assembly assembly,string typeName,string methodName,Type formatterType,MethodInfo renderMethod,MethodInfo numericMethod)
{
    var type=assembly.GetType(typeName)!; var method=type.GetMethod(methodName,BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance,[formatterType,typeof(string)]);
    if(method is null) return false;
    var instance=method!.IsStatic?null:Activator.CreateInstance(type);
    var formatterValue=Activator.CreateInstance(formatterType)!;
    var first=method.Invoke(instance,[formatterValue,"entry"]) as string;
    var second=method.Invoke(instance,[formatterValue,"second"]) as string;
    return ReturnsTargetResults(method,renderMethod,numericMethod)&&first=="impact:entry:1|number:7"&&second=="impact:second:2|number:7"&&StringCallCount(formatterValue)==2;
}
bool HasStringFormat(Type type)=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Any(static method=>(method.Name=="Format"||method.Name.EndsWith(".Format",StringComparison.Ordinal))&&method.GetParameters() is [{ ParameterType: var parameter },..]&&parameter==typeof(string));
static int StringCallCount(object formatter)=>(int)formatter.GetType().GetField("stringCallCount",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(formatter)!;
static bool ReturnsTargetResults(MethodInfo caller,MethodInfo firstTarget,MethodInfo secondTarget)
{
    var bytes=caller.GetMethodBody()?.GetILAsByteArray()??[];
    var stack=new List<int>(); var locals=new Dictionary<int,int>();
    for(var index=0;index<bytes.Length;index+=InstructionLength(bytes,index))
    {
        var opcode=bytes[index];
        switch(opcode)
        {
            case >=0x02 and <=0x05:
            case 0x0e:
            case 0x0f:
            case 0x12:
            case 0x14:
            case >=0x15 and <=0x20:
            case 0x72:
                stack.Add(0); break;
            case >=0x06 and <=0x09:
                stack.Add(locals.GetValueOrDefault(opcode-0x06)); break;
            case 0x11:
                stack.Add(locals.GetValueOrDefault(bytes[index+1])); break;
            case >=0x0a and <=0x0d:
                locals[opcode-0x0a]=Pop(stack); break;
            case 0x13:
                locals[bytes[index+1]]=Pop(stack); break;
            case 0x10:
            case 0x26:
                Pop(stack); break;
            case 0x25:
                stack.Add(stack.Count==0?0:stack[^1]); break;
            case 0x28:
            case 0x6f:
            {
                try
                {
                    var called=caller.Module.ResolveMethod(BitConverter.ToInt32(bytes,index+1));
                    if(called is null) return false;
                    var inputs=0;
                    var count=called.GetParameters().Length+(called.IsStatic?0:1);
                    for(var argument=0;argument<count;argument++) inputs|=Pop(stack);
                    if(called.Module==firstTarget.Module&&called.MetadataToken==firstTarget.MetadataToken) stack.Add(1);
                    else if(called.Module==secondTarget.Module&&called.MetadataToken==secondTarget.MetadataToken) stack.Add(2);
                    else if(called is MethodInfo method&&method.ReturnType!=typeof(void)) stack.Add(inputs);
                }
                catch(ArgumentException) { return false; }
                break;
            }
            case 0x2a:
                return (Pop(stack)&3)==3;
        }
    }
    return false;
}
static int Pop(List<int> stack)
{
    if(stack.Count==0) return 0;
    var value=stack[^1]; stack.RemoveAt(stack.Count-1); return value;
}
static int InstructionLength(byte[] bytes,int index)=>bytes[index] switch
{
    0x28 or 0x6f or 0x72 or 0x20 or 0x38 or 0x39 or 0x3a or 0x3b or 0x3c or 0x3d or 0x3e or 0x3f or 0x40 or 0x41 or 0x42 or 0x43 or 0x44=>5,
    0x0e or 0x0f or 0x10 or 0x11 or 0x12 or 0x13 or 0x1f or 0x2b or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37=>2,
    _=>1,
};
static int Reject(string message){Console.Error.WriteLine(message);Console.WriteLine("semantic-oracle: rejected");return 1;}
