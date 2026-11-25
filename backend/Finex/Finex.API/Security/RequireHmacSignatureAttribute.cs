namespace Finex.API.Security
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RequireHmacSignatureAttribute : Attribute
    {
    }
}