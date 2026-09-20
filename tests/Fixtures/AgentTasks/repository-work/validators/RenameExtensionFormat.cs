using System.Reflection;
using System.Runtime.CompilerServices;
var models=Assembly.Load("Models"); var consumers=Assembly.Load("Consumers");
var extensions=models.GetType("SemanticExtensionMethods.Models.MessageExtensions")!; var messageType=models.GetType("SemanticExtensionMethods.Models.Message")!; var instance=models.GetType("SemanticExtensionMethods.Models.InstanceFormatter")!; var staticFormatter=models.GetType("SemanticExtensionMethods.Models.StaticFormatter")!;
var render=extensions.GetMethod("Render",BindingFlags.Public|BindingFlags.Static,[models.GetType("SemanticExtensionMethods.Models.Message")!,typeof(string)]);
if (render?.ReturnType!=typeof(string) || !render.IsDefined(typeof(ExtensionAttribute)) || HasFormat(extensions) || HasFormatExtension(models,messageType)) return Reject("Only the extension member may expose Render(string).");
if (HasDeclaredMember(messageType,"Render") || HasDeclaredMember(messageType,"Format") || !HasPublicFormat(instance,false) || !HasPublicFormat(staticFormatter,true) || instance.GetMethod("Render") is not null || staticFormatter.GetMethod("Render") is not null) return Reject("The call must remain bound to the extension and the Format decoys must remain unchanged.");
var message=Activator.CreateInstance(messageType)!;
if (render.Invoke(null,[message,"entry"]) as string!="extension:entry") return Reject("Extension behavior changed.");
var view=Activator.CreateInstance(consumers.GetType("SemanticExtensionMethods.Consumers.FormatView")!)!;
var create=view.GetType().GetMethod("Create")!;
if (!CallsTargetExtension(create,render) || create.Invoke(view,[message,"entry"]) as string!="extension:entry|instance:entry|static:entry") return Reject("Extension call site or decoy behavior changed.");
Console.WriteLine("semantic-oracle: verified"); return 0;
bool HasFormat(Type type)=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Any(static m=>m.Name=="Format"||m.Name.EndsWith(".Format",StringComparison.Ordinal));
bool HasFormatExtension(Assembly assembly,Type message)=>assembly.GetTypes().SelectMany(static type=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.DeclaredOnly)).Any(method=>(method.Name=="Format"||method.Name.EndsWith(".Format",StringComparison.Ordinal))&&method.IsDefined(typeof(ExtensionAttribute))&&method.GetParameters() is [{ ParameterType: var parameterType },..]&&parameterType==message);
bool HasDeclaredMember(Type type,string name)=>type.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Any(member=>member.Name==name||member.Name.EndsWith($".{name}",StringComparison.Ordinal));
bool HasPublicFormat(Type type,bool isStatic)=>type.GetMethod("Format",BindingFlags.Public|(isStatic?BindingFlags.Static:BindingFlags.Instance),[typeof(string)])?.ReturnType==typeof(string);
static bool CallsTargetExtension(MethodInfo caller,MethodInfo target)
{
    var bytes=caller.GetMethodBody()?.GetILAsByteArray() ?? [];
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
