using System.Reflection;

var models = Assembly.Load("Models");
var consumers = Assembly.Load("Consumers");
var baseType = models.GetType("SemanticGenericInheritance.Models.MessageEnvelope`1");
var oldBaseType = models.GetType("SemanticGenericInheritance.Models.GenericEnvelope`1");
if (baseType is null || oldBaseType is not null)
{
    return Reject("Only the specified generic base type may remain renamed.");
}

var audit = RequireType(models, "SemanticGenericInheritance.Models.AuditEnvelope");
var customer = RequireType(models, "SemanticGenericInheritance.Models.CustomerEnvelope");
var shadow = RequireType(models, "SemanticGenericInheritance.Models.ShadowEnvelope");
if (!UsesBase(audit, baseType) || !UsesBase(customer, baseType) || !UsesBase(shadow, baseType))
{
    return Reject("Every descendant must retain the renamed constructed generic base type.");
}

var shadowLabel = shadow.GetMethod("Label", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, [typeof(string)]);
if (shadowLabel?.ReturnType != typeof(string) || shadow.GetMethod("Describe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, [typeof(string)]) is not null)
{
    return Reject("The hidden ShadowEnvelope.Label(string) member must remain unchanged.");
}
if (shadowLabel.Invoke(Activator.CreateInstance(shadow), ["entry"]) as string != "shadow:entry")
{
    return Reject("The hidden ShadowEnvelope.Label(string) behavior changed.");
}

var presenterType = RequireType(consumers, "SemanticGenericInheritance.Consumers.EnvelopePresenter");
var auditRecord = RequireType(models, "SemanticGenericInheritance.Models.AuditRecord");
var customerRecord = RequireType(models, "SemanticGenericInheritance.Models.CustomerRecord");
if (!UsesParameter(presenterType, "PresentAudit", baseType.MakeGenericType(auditRecord)) ||
    !UsesParameter(presenterType, "PresentCustomer", baseType.MakeGenericType(customerRecord)) ||
    !UsesParameter(presenterType, "PresentShadow", shadow))
{
    return Reject("Every consumer type reference must use the exact renamed generic type.");
}

var presenter = Activator.CreateInstance(presenterType)!;
if (Invoke(presenterType, presenter, "PresentAudit", Activator.CreateInstance(audit)!, "entry") != "base:AuditRecord:entry" ||
    Invoke(presenterType, presenter, "PresentCustomer", Activator.CreateInstance(customer)!, "entry") != "base:CustomerRecord:entry" ||
    Invoke(presenterType, presenter, "PresentShadow", Activator.CreateInstance(shadow)!, "entry") != "shadow:entry")
{
    return Reject("Generic or hidden-member behavior changed.");
}

Console.WriteLine("semantic-oracle: verified");
return 0;

static Type RequireType(Assembly assembly, string name) => assembly.GetType(name) ?? throw new InvalidOperationException($"Missing type '{name}'.");
static bool UsesBase(Type type, Type baseType) => type.BaseType?.IsGenericType == true && type.BaseType.GetGenericTypeDefinition() == baseType;
static bool UsesParameter(Type type, string methodName, Type parameterType) => type.GetMethod(methodName)?.GetParameters() is [{ ParameterType: var actual }, { ParameterType: var value }] && actual == parameterType && value == typeof(string);
static string? Invoke(Type type, object instance, string methodName, object envelope, string value) => type.GetMethod(methodName)?.Invoke(instance, [envelope, value]) as string;
static int Reject(string message) { Console.Error.WriteLine(message); Console.WriteLine("semantic-oracle: rejected"); return 1; }
