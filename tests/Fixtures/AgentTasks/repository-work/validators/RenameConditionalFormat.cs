using System.Reflection;
var expected=AppContext.TargetFrameworkName!.Contains("Version=v8.0",StringComparison.Ordinal)?"legacy:entry|number:7":"modern:entry|number:7";
var models=Assembly.Load("Models"); var consumers=Assembly.Load("Consumers"); var formatter=models.GetType("SemanticMultiTargetConditional.Models.ConditionalFormatter")!;
var render=formatter.GetMethod("Render",BindingFlags.Public|BindingFlags.Instance,[typeof(string)]);
var numericFormat=formatter.GetMethod("Format",BindingFlags.Public|BindingFlags.Instance,[typeof(int)]);
if(render?.ReturnType!=typeof(string)||numericFormat?.ReturnType!=typeof(string)||HasStringFormat(formatter)) return Reject("The conditional string member or numeric overload changed incorrectly.");
var value=Activator.CreateInstance(formatter)!;
if(render.Invoke(value,["entry"]) as string!=expected.Split('|')[0]||numericFormat.Invoke(value,[7]) as string!="number:7") return Reject("Conditional behavior changed.");
var view=Activator.CreateInstance(consumers.GetType("SemanticMultiTargetConditional.Consumers.ConditionalView")!)!;
var create=view.GetType().GetMethod("Create",BindingFlags.Public|BindingFlags.Instance,[formatter,typeof(string)]);
if(create is null||!CallsTarget(create,render)||!CallsTarget(create,numericFormat)||create.Invoke(view,[value,"entry"]) as string!=expected) return Reject("Conditional call-site behavior changed.");
Console.WriteLine("semantic-oracle: verified"); return 0;
bool HasStringFormat(Type type)=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Any(static method=>(method.Name=="Format"||method.Name.EndsWith(".Format",StringComparison.Ordinal))&&method.GetParameters() is [{ ParameterType: var parameter },..]&&parameter==typeof(string));
static bool CallsTarget(MethodInfo caller,MethodInfo target)
{
    var bytes=caller.GetMethodBody()?.GetILAsByteArray()??[];
    for(var index=0;index<=bytes.Length-5;index++)
    {
        if(bytes[index] is not (0x28 or 0x6f)) continue;
        try
        {
            var called=caller.Module.ResolveMethod(BitConverter.ToInt32(bytes,index+1));
            if(called is not null&&called.Module==target.Module&&called.MetadataToken==target.MetadataToken) return true;
        }
        catch(ArgumentException) { }
    }
    return false;
}
static int Reject(string message){Console.Error.WriteLine(message);Console.WriteLine("semantic-oracle: rejected");return 1;}
