namespace ShutkiVorta.Application.Common.Security;

/// <summary>
/// Admin pages and handlers only Admins may use (settings, staff management). Requests use <see cref="IRequireAdmin"/>.
/// A plain attribute on purpose: Razor Pages ignores filter attributes on handler methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AdminOnlyAttribute : Attribute;
