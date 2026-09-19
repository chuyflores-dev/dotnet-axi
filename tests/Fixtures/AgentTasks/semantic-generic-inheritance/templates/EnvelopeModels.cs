namespace SemanticGenericInheritance.Models;
public sealed record AuditRecord;
public sealed record CustomerRecord;
public sealed record ShadowRecord;
public class GenericEnvelope<T> { public string Label(string value) => $"base:{typeof(T).Name}:{value}"; }
public sealed class AuditEnvelope : GenericEnvelope<AuditRecord> { }
public sealed class CustomerEnvelope : GenericEnvelope<CustomerRecord> { }
public sealed class ShadowEnvelope : GenericEnvelope<ShadowRecord> { public new string Label(string value) => $"shadow:{value}"; }
