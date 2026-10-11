using System.Reflection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Razor.Hosting;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Web.Infrastructure.Security;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// Every compiled page under /Pages/Admin states who may open it, and its handlers can actually be checked. Reads the
/// compiled Razor items, so a page without code-behind is covered too.
/// </summary>
public sealed class PageAccessCoverageTests
{
    private static readonly IReadOnlyList<(string Path, Type PageModel)> AdminPages = new RazorCompiledItemLoader()
        .LoadItems(typeof(Program).Assembly)
        .Where(i => i.Kind == "mvc.1.0.razor-page" && i.Identifier.StartsWith("/Pages/Admin/", StringComparison.OrdinalIgnoreCase))
        .Select(i => (i.Identifier, ModelType(i.Type)))
        .ToList();

    public static TheoryData<string> AdminPagePaths()
    {
        var data = new TheoryData<string>();
        foreach (var (path, _) in AdminPages)
        {
            data.Add(path);
        }

        return data;
    }

    [Fact]
    public void TheAdminPagesAreFound()
    {
        Assert.Contains(AdminPages, p => p.Path == "/Pages/Admin/Index.cshtml");
        Assert.Contains(AdminPages, p => p.Path == "/Pages/Admin/Settings/Edit.cshtml");
        Assert.True(AdminPages.Count >= 20);
    }

    [Theory]
    [MemberData(nameof(AdminPagePaths))]
    public void EveryAdminPage_HasExactlyOneAccessAttribute(string path)
    {
        var model = AdminPages.Single(p => p.Path == path).PageModel;
        var rules = PermissionPageFilter.RulesFor(model);

        Assert.True(rules.Length == 1, $"{path} ({model.Name}) needs exactly one of [RequirePermission], [AdminOnly] or [AnyStaff]; found {rules.Length}.");
    }

    [Theory]
    [MemberData(nameof(AdminPagePaths))]
    public void AdminHandlers_UseOnlyPlainAttributes_AndPageModelsDoNotRunCodeBeforeTheFilter(string path)
    {
        var model = AdminPages.Single(p => p.Path == path).PageModel;

        // Razor Pages ignores filter attributes on handler methods, so a permission there must be a plain attribute.
        foreach (var handler in Handlers(model))
        {
            var filters = handler.GetCustomAttributes(inherit: true).OfType<IFilterMetadata>().ToList();
            Assert.True(filters.Count == 0, $"{model.Name}.{handler.Name} has filter attributes that would be ignored: {string.Join(", ", filters)}");
        }

        // The permission filter runs first (order int.MinValue); page-model hooks would run before the handler but are not needed.
        foreach (var hook in new[] { "OnPageHandlerExecuting", "OnPageHandlerExecutionAsync", "OnPageHandlerSelected", "OnPageHandlerSelectionAsync" })
        {
            var overridden = model.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == hook && m.DeclaringType != typeof(PageModel) && m.DeclaringType != typeof(object));
            Assert.Empty(overridden);
        }
    }

    /// <summary>On a page anyone with "view" may open, every action handler says what it needs (usually the "manage" permission).</summary>
    [Theory]
    [MemberData(nameof(AdminPagePaths))]
    public void PostHandlersOnViewPages_StateTheirOwnRequirement(string path)
    {
        var model = AdminPages.Single(p => p.Path == path).PageModel;
        if (PermissionPageFilter.RulesFor(model).SingleOrDefault() is not RequirePermissionAttribute { } page
            || !page.Permission.EndsWith(".view", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var handler in Handlers(model).Where(h => h.Name.StartsWith("OnPost", StringComparison.Ordinal)))
        {
            Assert.True(PermissionPageFilter.RulesFor(handler).Length > 0,
                $"{model.Name}.{handler.Name} changes something on a {page.Permission} page but has no [RequirePermission] or [AdminOnly].");
        }
    }

    [Fact]
    public void AccessAttributes_AreNotFilters()
    {
        Assert.False(typeof(IFilterMetadata).IsAssignableFrom(typeof(RequirePermissionAttribute)));
        Assert.False(typeof(IFilterMetadata).IsAssignableFrom(typeof(AdminOnlyAttribute)));
        Assert.False(typeof(IFilterMetadata).IsAssignableFrom(typeof(AnyStaffAttribute)));
    }

    [Fact]
    public void TheExistingPages_HaveTheirAgreedAccess()
    {
        string Rule(string path) => PermissionPageFilter.RulesFor(AdminPages.Single(p => p.Path == path).PageModel).Single() switch
        {
            RequirePermissionAttribute r => r.Permission,
            AdminOnlyAttribute => "admin",
            AnyStaffAttribute => "any staff",
            var other => other.ToString()!,
        };

        Assert.Equal("any staff", Rule("/Pages/Admin/Index.cshtml"));
        Assert.Equal(Permissions.OrdersView, Rule("/Pages/Admin/Orders/Details.cshtml"));
        Assert.Equal(Permissions.RestaurantsView, Rule("/Pages/Admin/Recurring/Statement.cshtml"));
        Assert.Equal(Permissions.ProductionView, Rule("/Pages/Admin/Production/Index.cshtml"));
        Assert.Equal(Permissions.MenuView, Rule("/Pages/Admin/Menu/Edit.cshtml"));
        Assert.Equal(Permissions.InventoryView, Rule("/Pages/Admin/Inventory/Receipt.cshtml"));
        Assert.Equal(Permissions.InquiriesView, Rule("/Pages/Admin/Inquiries/Index.cshtml"));
        Assert.Equal(Permissions.EmailsView, Rule("/Pages/Admin/Emails/Details.cshtml"));
        Assert.Equal("admin", Rule("/Pages/Admin/Settings/Index.cshtml"));
        Assert.Equal("admin", Rule("/Pages/Admin/Settings/Edit.cshtml"));
    }

    /// <summary>The page model (the @model type when it is a PageModel, otherwise the generated page itself).</summary>
    private static Type ModelType(Type pageType)
    {
        var model = pageType.GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)?.PropertyType;
        return model is not null && typeof(PageModel).IsAssignableFrom(model) ? model : pageType;
    }

    private static IEnumerable<MethodInfo> Handlers(Type model) =>
        model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("OnGet", StringComparison.Ordinal) || m.Name.StartsWith("OnPost", StringComparison.Ordinal));
}
