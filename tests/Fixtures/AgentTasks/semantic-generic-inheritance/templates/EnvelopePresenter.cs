using SemanticGenericInheritance.Models;
namespace SemanticGenericInheritance.Consumers;
public sealed class EnvelopePresenter
{
    public string PresentAudit(GenericEnvelope<AuditRecord> envelope, string value) => envelope.Label(value);
    public string PresentCustomer(GenericEnvelope<CustomerRecord> envelope, string value) => envelope.Label(value);
    public string PresentShadow(ShadowEnvelope envelope, string value) => envelope.Label(value);
}
