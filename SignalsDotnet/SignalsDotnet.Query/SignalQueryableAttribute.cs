namespace SignalsDotnet.Query;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SignalQueryableAttribute : Attribute
{
}
