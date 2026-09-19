namespace SemanticVirtualOverrides.Models;
public class MessageFormatter { private int calls; public int CallCount => calls; protected void RecordCall() => calls++; public virtual string Format(string value) { RecordCall(); return $"base:{value}"; } }
public class AuditFormatter : MessageFormatter { public override string Format(string value) { RecordCall(); return $"audit:{value}"; } }
public sealed class WorkerFormatter : AuditFormatter { public override string Format(string value) { RecordCall(); return $"worker:{value}"; } }
public sealed class ShadowFormatter : MessageFormatter { public new string Format(string value) => $"shadow:{value}"; }
