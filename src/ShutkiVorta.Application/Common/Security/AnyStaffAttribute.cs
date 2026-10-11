namespace ShutkiVorta.Application.Common.Security;

/// <summary>
/// An admin page every staff member may open (e.g. the time clock); the page itself decides what to show.
/// Requests use <see cref="IRequireStaff"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AnyStaffAttribute : Attribute;
