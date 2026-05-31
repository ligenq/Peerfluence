namespace Peerfluence.Core.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true)]
public class ExcludeFromDIAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class SingletonServiceAttribute : Attribute
{
}
