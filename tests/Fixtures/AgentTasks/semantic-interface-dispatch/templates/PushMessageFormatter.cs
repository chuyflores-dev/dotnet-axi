using SemanticInterfaceDispatch.Contracts;
namespace SemanticInterfaceDispatch.Implementations;
public sealed class PushMessageFormatter : IMessageFormatter { public string Format(string value) => $"push:{value}"; }
