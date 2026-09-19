using SemanticVirtualOverrides.Models;
namespace SemanticVirtualOverrides.Consumers;
public sealed class FormatterPresenter
{
    public string RenderBase(MessageFormatter baseFormatter, string value) => baseFormatter.Format(value);
    public string RenderAudit(AuditFormatter auditFormatter, string value) => auditFormatter.Format(value);
    public string RenderWorker(WorkerFormatter workerFormatter, string value) => workerFormatter.Format(value);
    public string RenderShadow(ShadowFormatter shadowFormatter, string value) => shadowFormatter.Format(value);
}
