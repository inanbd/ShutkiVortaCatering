using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Web.Infrastructure.Security;

/// <summary>
/// Enforces <see cref="RequirePermissionAttribute"/>, <see cref="AdminOnlyAttribute"/> and <see cref="AnyStaffAttribute"/> on
/// admin pages: the page model's attribute AND the selected handler's attribute must both be satisfied. A refusal is a
/// plain 403, rendered by the error page. Registered globally by type with order int.MinValue, so it runs before any page
/// model code; pages under /Admin without an attribute are refused (fail closed).
/// </summary>
public sealed class PermissionPageFilter(ICurrentStaff currentStaff, ILogger<PermissionPageFilter> logger) : IAsyncPageFilter
{
    private static readonly ConcurrentDictionary<MemberInfo, Attribute[]> Rules = new();

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor;
        var pageRules = RulesFor(descriptor.HandlerTypeInfo);
        var handlerRules = context.HandlerMethod is { } handler ? RulesFor(handler.MethodInfo) : [];

        if (pageRules.Length == 0 && handlerRules.Length == 0)
        {
            if (descriptor.ViewEnginePath.StartsWith("/Admin/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("Admin page {Page} has no RequirePermission, AdminOnly or AnyStaff attribute; refusing it", descriptor.ViewEnginePath);
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                return;
            }

            await next();
            return;
        }

        var access = await currentStaff.GetAsync(context.HttpContext.RequestAborted);
        if (!pageRules.All(r => Allows(r, access)) || !handlerRules.All(r => Allows(r, access)))
        {
            logger.LogInformation("Refused {Page} ({Handler}) to user {UserId} ({Role})",
                descriptor.ViewEnginePath, context.HandlerMethod?.Name ?? "no handler", access.UserId, access.Role);
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        await next();
    }

    /// <summary>The access attributes on a page model class or handler method.</summary>
    public static Attribute[] RulesFor(MemberInfo member) =>
        Rules.GetOrAdd(member, m => m.GetCustomAttributes(inherit: true)
            .OfType<Attribute>()
            .Where(a => a is RequirePermissionAttribute or AdminOnlyAttribute or AnyStaffAttribute)
            .ToArray());

    public static bool Allows(Attribute rule, StaffAccess access) => rule switch
    {
        AnyStaffAttribute => access.IsStaff,
        AdminOnlyAttribute => access.IsAdmin,
        RequirePermissionAttribute required => access.Can(required.Permission),
        _ => false,
    };
}
