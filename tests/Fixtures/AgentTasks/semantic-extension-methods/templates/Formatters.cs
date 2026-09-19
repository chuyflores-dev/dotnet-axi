namespace SemanticExtensionMethods.Models;
public sealed class Message;
public sealed class InstanceFormatter { public string Format(string value) => $"instance:{value}"; }
public static class StaticFormatter { public static string Format(string value) => $"static:{value}"; }
public static class MessageExtensions { public static string Format(this Message message, string value) => $"extension:{value}"; }
