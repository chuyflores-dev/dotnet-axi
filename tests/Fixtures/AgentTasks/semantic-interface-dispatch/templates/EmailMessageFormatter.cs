using SemanticInterfaceDispatch.Contracts;
namespace SemanticInterfaceDispatch.Implementations;
public sealed class EmailMessageFormatter : IMessageFormatter { public string Format(string value) => $"email:{value}"; }
