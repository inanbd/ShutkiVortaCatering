using Microsoft.AspNetCore.Razor.TagHelpers;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Web.Infrastructure.Security;

/// <summary>
/// <c>requires-permission="@Permissions.MenuManage"</c> on any element: the element (with everything in it) is only rendered
/// for staff with that permission. Use it for buttons and forms of actions the person cannot take.
/// </summary>
[HtmlTargetElement(Attributes = AttributeName)]
public sealed class RequiresPermissionTagHelper(ICurrentStaff currentStaff) : TagHelper
{
    private const string AttributeName = "requires-permission";

    // Last, so that suppressing also removes what other tag helpers added (e.g. a form's antiforgery token).
    public override int Order => int.MaxValue;

    [HtmlAttributeName(AttributeName)]
    public string Permission { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!(await currentStaff.GetAsync()).Can(Permission))
        {
            output.SuppressOutput();
        }
    }
}

/// <summary>
/// <c>link-permission="@Permissions.OrdersView"</c> on a link to another admin module: without that permission the link's
/// content is rendered as plain text instead of a link the person could not open.
/// </summary>
[HtmlTargetElement("a", Attributes = AttributeName)]
public sealed class PermissionLinkTagHelper(ICurrentStaff currentStaff) : TagHelper
{
    private const string AttributeName = "link-permission";

    public override int Order => int.MaxValue;

    [HtmlAttributeName(AttributeName)]
    public string Permission { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!(await currentStaff.GetAsync()).Can(Permission))
        {
            output.TagName = null;
        }
    }
}
