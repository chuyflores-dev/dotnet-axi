using SemanticInterfaceDispatch.Contracts;
namespace SemanticInterfaceDispatch.Implementations;
public sealed class SmsMessageFormatter : IMessageFormatter { public string Format(string value) => $"sms:{value}"; }
