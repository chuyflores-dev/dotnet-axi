using SemanticExtensionMethods.Models;
namespace SemanticExtensionMethods.Consumers;
public sealed class FormatView { public string Create(Message message, string value) => $"{message.Format(value)}|{new InstanceFormatter().Format(value)}|{StaticFormatter.Format(value)}"; }
