using SemanticInterfaceDispatch.Contracts;
namespace SemanticInterfaceDispatch.Consumers;
public sealed class MessageDispatcher(IMessageFormatter email, IMessageFormatter sms, IMessageFormatter push)
{
    public string DispatchEmail(string value) => email.Format(value);
    public string DispatchSms(string value) => sms.Format(value);
    public string DispatchPush(string value) => push.Format(value);
}
