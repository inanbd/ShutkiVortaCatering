namespace ShutkiVorta.Application.Common.Security;

/// <summary>Identity role names. The seeder creates every role in <see cref="All"/> on each start.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string Customer = "Customer";

    /// <summary>The roles that open the admin area (with a confirmed email). A person holds at most one of them.</summary>
    public static readonly IReadOnlyList<string> Staff = [Admin, Manager, Employee];

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Employee, Customer];
}
