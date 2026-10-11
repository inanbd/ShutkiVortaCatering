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

    /// <summary>On a page anyone with "view" may open, every action handler says what it needs, and it is more than "view".</summary>
    [Theory]
    [MemberData(nameof(AdminPagePaths))]
    public void ActionHandlersOnViewPages_StateTheirOwnRequirement(string path)
    {
        var model = AdminPages.Single(p => p.Path == path).PageModel;
        if (PermissionPageFilter.RulesFor(model).SingleOrDefault() is not RequirePermissionAttribute { } page
            || !page.Permission.EndsWith(".view", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var handler in Handlers(model).Where(h => !h.Name.StartsWith("OnGet", StringComparison.Ordinal)))
        {
            var rules = PermissionPageFilter.RulesFor(handler);
            Assert.True(rules.Length > 0,
                $"{model.Name}.{handler.Name} changes something on a {page.Permission} page but has no [RequirePermission] or [AdminOnly].");
            Assert.DoesNotContain(rules, r => r is RequirePermissionAttribute { Permission: var p } && p.EndsWith(".view", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AccessAttributes_AreNotFilters()
    {
        Assert.False(typeof(IFilterMetadata).IsAssignableFrom(typeof(RequirePermissionAttribute)));
        Assert.False(typeof(IFilterMetadata).IsAssignableFrom(typeof(AdminOnlyAttribute)));
        Assert.False(typeof(IFilterMetadata).IsAssignableFrom(typeof(AnyStaffAttribute)));
    }

    /// <summary>The agreed access table for the pages that existed before staff roles (pages and every action handler).</summary>
    private static readonly IReadOnlyDictionary<string, string> AgreedAccess = new Dictionary<string, string>
    {
        ["/Pages/Admin/Index.cshtml"] = "any staff",
        ["/Pages/Admin/Orders/Index.cshtml"] = Permissions.OrdersView,
        ["/Pages/Admin/Orders/Details.cshtml"] = Permissions.OrdersView,
        ["/Pages/Admin/Orders/Details.cshtml OnPostStatusAsync"] = Permissions.OrdersManage,
        ["/Pages/Admin/Orders/Details.cshtml OnPostNotesAsync"] = Permissions.OrdersManage,
        ["/Pages/Admin/Recurring/Index.cshtml"] = Permissions.RestaurantsView,
        ["/Pages/Admin/Recurring/Index.cshtml OnPostGenerateAsync"] = Permissions.RestaurantsManage,
        ["/Pages/Admin/Recurring/Details.cshtml"] = Permissions.RestaurantsView,
        ["/Pages/Admin/Recurring/Details.cshtml OnPostStatusAsync"] = Permissions.RestaurantsManage,
        ["/Pages/Admin/Recurring/Details.cshtml OnPostSkipAsync"] = Permissions.RestaurantsManage,
        ["/Pages/Admin/Recurring/Details.cshtml OnPostTermsAsync"] = Permissions.RestaurantsManage,
        ["/Pages/Admin/Recurring/Statement.cshtml"] = Permissions.RestaurantsView,
        ["/Pages/Admin/Production/Index.cshtml"] = Permissions.ProductionView,
        ["/Pages/Admin/Menu/Index.cshtml"] = Permissions.MenuView,
        ["/Pages/Admin/Menu/Index.cshtml OnPostAvailabilityAsync"] = Permissions.MenuManage,
        ["/Pages/Admin/Menu/Index.cshtml OnPostDeleteAsync"] = Permissions.MenuManage,
        ["/Pages/Admin/Menu/Edit.cshtml"] = Permissions.MenuView,
        ["/Pages/Admin/Menu/Edit.cshtml OnPostAsync"] = Permissions.MenuManage,
        ["/Pages/Admin/Inventory/Index.cshtml"] = Permissions.InventoryView,
        ["/Pages/Admin/Inventory/Edit.cshtml"] = Permissions.InventoryView,
        ["/Pages/Admin/Inventory/Edit.cshtml OnPostAsync"] = Permissions.InventoryManage,
        ["/Pages/Admin/Inventory/Edit.cshtml OnPostDeleteAsync"] = Permissions.InventoryManage,
        ["/Pages/Admin/Inventory/Items.cshtml"] = Permissions.InventoryView,
        ["/Pages/Admin/Inventory/Items.cshtml OnPostAddAsync"] = Permissions.InventoryManage,
        ["/Pages/Admin/Inventory/Item.cshtml"] = Permissions.InventoryView,
        ["/Pages/Admin/Inventory/Item.cshtml OnPostAsync"] = Permissions.InventoryManage,
        ["/Pages/Admin/Inventory/Item.cshtml OnPostDeleteAsync"] = Permissions.InventoryManage,
        ["/Pages/Admin/Inventory/Receipt.cshtml"] = Permissions.InventoryView,
        ["/Pages/Admin/Inquiries/Index.cshtml"] = Permissions.InquiriesView,
        ["/Pages/Admin/Inquiries/Index.cshtml OnPostHandledAsync"] = Permissions.InquiriesManage,
        ["/Pages/Admin/Emails/Index.cshtml"] = Permissions.EmailsView,
        ["/Pages/Admin/Emails/Index.cshtml OnPostRetryAsync"] = Permissions.EmailsManage,
        ["/Pages/Admin/Emails/Index.cshtml OnPostRetryFailedAsync"] = Permissions.EmailsManage,
        ["/Pages/Admin/Emails/Details.cshtml"] = Permissions.EmailsView,
        ["/Pages/Admin/Emails/Details.cshtml OnPostRetryAsync"] = Permissions.EmailsManage,
        ["/Pages/Admin/Settings/Index.cshtml"] = "admin",
        ["/Pages/Admin/Settings/Edit.cshtml"] = "admin",
    };

    /// <summary>Pinned in both directions: every listed page/handler has this rule, and the listed pages have no other action handlers.</summary>
    [Fact]
    public void TheExistingPages_HaveTheirAgreedAccess()
    {
        static string Describe(Attribute rule) => rule switch
        {
            RequirePermissionAttribute r => r.Permission,
            AdminOnlyAttribute => "admin",
            AnyStaffAttribute => "any staff",
            var other => other.ToString()!,
        };

        foreach (var (key, expected) in AgreedAccess)
        {
            var parts = key.Split(' ');
            var model = AdminPages.Single(p => p.Path == parts[0]).PageModel;
            var member = parts.Length == 1 ? (MemberInfo)model : Handlers(model).Single(h => h.Name == parts[1]);
            Assert.Equal(expected, Describe(PermissionPageFilter.RulesFor(member).Single()));
        }

        // Pages anyone with "view" may open need a rule on each action handler; Admin-only pages are covered by the page rule.
        foreach (var page in AgreedAccess.Where(e => !e.Key.Contains(' ') && e.Value.EndsWith(".view", StringComparison.Ordinal)).Select(e => e.Key))
        {
            var model = AdminPages.Single(p => p.Path == page).PageModel;
            foreach (var handler in Handlers(model).Where(h => !h.Name.StartsWith("OnGet", StringComparison.Ordinal)))
            {
                Assert.True(AgreedAccess.ContainsKey($"{page} {handler.Name}"), $"{page} {handler.Name} is not in the agreed access table.");
            }
        }
    }

    /// <summary>The page model (the @model type when it is a PageModel, otherwise the generated page itself).</summary>
    private static Type ModelType(Type pageType)
    {
        var model = pageType.GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)?.PropertyType;
        return model is not null && typeof(PageModel).IsAssignableFrom(model) ? model : pageType;
    }

    /// <summary>Razor Pages handler methods: On{Verb}[Name][Async] for any HTTP verb.</summary>
    private static IEnumerable<MethodInfo> Handlers(Type model) =>
        model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => new[] { "OnGet", "OnPost", "OnPut", "OnDelete", "OnPatch" }.Any(verb => m.Name.StartsWith(verb, StringComparison.Ordinal)));
}
