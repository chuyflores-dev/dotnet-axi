using System.Reflection;
var models = Assembly.Load("Models"); var consumers = Assembly.Load("Consumers");
var baseType = TypeOf("MessageFormatter"); var audit = TypeOf("AuditFormatter"); var worker = TypeOf("WorkerFormatter"); var shadow = TypeOf("ShadowFormatter");
if (!HasOnlyRender(baseType) || !IsOverride(audit, baseType) || !IsOverride(worker, baseType)) return Reject("The virtual member and every exact override must expose only Render(string).");
var hidden = shadow.GetMethod("Format", BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly, [typeof(string)]);
if (hidden?.Invoke(Activator.CreateInstance(shadow), ["entry"]) as string != "shadow:entry" || shadow.GetMethod("Render", BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly, [typeof(string)]) is not null) return Reject("The hidden Format(string) member must remain unchanged.");
var presenterType = consumers.GetType("SemanticVirtualOverrides.Consumers.FormatterPresenter")!; var presenter = Activator.CreateInstance(presenterType)!;
object? lastInstance = null;
if (!Verify("RenderBase", baseType, "base:entry") || !Verify("RenderAudit", audit, "audit:entry") || !Verify("RenderWorker", worker, "worker:entry") || Invoke("RenderShadow", shadow) != "shadow:entry") return Reject("Virtual dispatch behavior changed or was bypassed.");
Console.WriteLine("semantic-oracle: verified"); return 0;
Type TypeOf(string name) => models.GetType($"SemanticVirtualOverrides.Models.{name}") ?? throw new InvalidOperationException();
bool HasOnlyRender(Type type) => type.GetMethod("Render", BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly, [typeof(string)])?.ReturnType == typeof(string) && !HasFormat(type);
bool IsOverride(Type type, Type baseType) { var render=type.GetMethod("Render", BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly,[typeof(string)]); return render?.GetBaseDefinition().DeclaringType==baseType && !HasFormat(type); }
bool HasFormat(Type type) => type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Any(static method => (method.Name == "Format" || method.Name.EndsWith(".Format", StringComparison.Ordinal)) && method.ReturnType == typeof(string) && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(string));
bool Verify(string name, Type type, string expected) => Invoke(name,type)==expected && (int)baseType.GetProperty("CallCount")!.GetValue(lastInstance!)! == 1;
string? Invoke(string name, Type type) { lastInstance=Activator.CreateInstance(type)!; return presenterType.GetMethod(name)!.Invoke(presenter,[lastInstance,"entry"]) as string; }
static int Reject(string message) { Console.Error.WriteLine(message); Console.WriteLine("semantic-oracle: rejected"); return 1; }
