using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Web.Services;

/// <summary>
/// Builds absolute URLs. Uses Site:BaseUrl when configured (recommended in production), otherwise the current request's host.
/// </summary>
public sealed class AppUrls(IOptionsMonitor<SiteOptions> site, IHttpContextAccessor httpContextAccessor) : IAppUrls
{
    public string BaseUrl
    {
        get
        {
            var configured = site.CurrentValue.BaseUrl?.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            var request = httpContextAccessor.HttpContext?.Request;
            return request is null ? "http://localhost" : $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
        }
    }

    public string Absolute(string relativePath)
    {
        if (relativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || relativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath;
        }

        return BaseUrl + "/" + relativePath.TrimStart('~').TrimStart('/');
    }

    public string Home() => BaseUrl + "/";

    public string Menu() => Absolute("/menu");

    public string MenuItem(string slug) => Absolute($"/menu/{slug}");

    public string OrderStatus(string orderNumber, string trackingToken) =>
        Absolute($"/order/{Uri.EscapeDataString(orderNumber)}?token={Uri.EscapeDataString(trackingToken)}");

    public string CustomerOrder(string orderNumber) => Absolute($"/account/orders/{Uri.EscapeDataString(orderNumber)}");

    public string AdminOrder(string orderNumber) => Absolute($"/admin/orders/{Uri.EscapeDataString(orderNumber)}");

    public string AdminInquiries() => Absolute("/admin/inquiries");

    public string ConfirmEmail(string userId, string code) =>
        Absolute($"/account/confirm-email?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}");

    public string ResetPassword(string email, string code) =>
        Absolute($"/account/reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}");

    public string Login() => Absolute("/account/login");
}
