using System.Reflection;
if (!HasLinkedPartialRenderDeclaration()) return Reject("The linked partial declaration must own Render(string).");
var checks=new[]{("Primary","SemanticPartialLinked.Primary.PrimaryView","primary"),("Secondary","SemanticPartialLinked.Secondary.SecondaryView","secondary")};
foreach(var (assemblyName,viewName,prefix) in checks)
{
    var assembly=Assembly.Load(assemblyName); var formatterType=assembly.GetType("SemanticPartialLinked.LinkedFormatter")!;
    var render=formatterType.GetMethod("Render",BindingFlags.Public|BindingFlags.Instance,[typeof(string)]);
    if(render?.ReturnType!=typeof(string)||HasStringFormat(formatterType)||!HasNumberFormat(formatterType)) return Reject("The linked partial member or its overload changed incorrectly.");
    var direct=Activator.CreateInstance(formatterType)!;
    if(render.Invoke(direct,["entry"]) as string!="linked:entry:1"||CallCount(direct)!=1) return Reject("The renamed partial-member behavior changed.");
    var view=Activator.CreateInstance(assembly.GetType(viewName)!)!; var create=view.GetType().GetMethod("Create",BindingFlags.Public|BindingFlags.Instance,[formatterType,typeof(string)])!;
    var caller=Activator.CreateInstance(formatterType)!;
    if(!CallsTarget(create,render)||create.Invoke(view,[caller,"entry"]) as string!=$"{prefix}:linked:entry:1|number:7"||CallCount(caller)!=1) return Reject("A linked-owner call site or behavior changed.");
}
Console.WriteLine("semantic-oracle: verified"); return 0;
bool HasLinkedPartialRenderDeclaration()
{
    var tokens=Tokenize(File.ReadAllText(Path.Combine("src","Shared","LinkedFormatter.Format.cs"))).ToArray();
    for(var index=0;index<=tokens.Length-5;index++)
    {
        if(tokens[index..(index+5)] is not ["public","sealed","partial","class","LinkedFormatter"]) continue;
        var open=Array.IndexOf(tokens,"{",index+5);
        if(open<0) return false;
        for(int cursor=open+1,depth=1;cursor<tokens.Length;cursor++)
        {
            if(tokens[cursor]=="{") { depth++; continue; }
            if(tokens[cursor]=="}") { if(--depth==0) break; continue; }
            if(depth==1&&cursor<=tokens.Length-5&&tokens[cursor..(cursor+5)] is ["public","string","Render","(","string"]) return true;
        }
    }
    return false;
}
static IEnumerable<string> Tokenize(string source)
{
    for(var index=0;index<source.Length;)
    {
        if(char.IsWhiteSpace(source[index])) { index++; continue; }
        if(source[index]=='/'&&index+1<source.Length&&source[index+1]=='/') { index=source.IndexOf('\n',index+2); if(index<0) yield break; continue; }
        if(source[index]=='/'&&index+1<source.Length&&source[index+1]=='*') { var end=source.IndexOf("*/",index+2,StringComparison.Ordinal); index=end<0?source.Length:end+2; continue; }
        if(source[index]=='@'&&index+1<source.Length&&source[index+1]=='\"') { index+=2; while(index<source.Length&&(source[index]!='\"'||++index>=source.Length||source[index]!='\"')) index++; index++; continue; }
        if(source[index]=='\"') { for(index++;index<source.Length;index++) if(source[index]=='\\') index++; else if(source[index++]=='\"') break; continue; }
        if(char.IsLetter(source[index])||source[index]=='_') { var start=index++; while(index<source.Length&&(char.IsLetterOrDigit(source[index])||source[index]=='_')) index++; yield return source[start..index]; continue; }
        yield return source[index++].ToString();
    }
}
bool HasStringFormat(Type type)=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Any(static method=>(method.Name=="Format"||method.Name.EndsWith(".Format",StringComparison.Ordinal))&&method.GetParameters() is [{ ParameterType: var type },..]&&type==typeof(string));
bool HasNumberFormat(Type type)=>type.GetMethod("Format",BindingFlags.Public|BindingFlags.Instance,[typeof(int)])?.ReturnType==typeof(string);
static int CallCount(object formatter)=>(int)formatter.GetType().GetProperty("CallCount",BindingFlags.Public|BindingFlags.Instance)!.GetValue(formatter)!;
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
