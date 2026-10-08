using System.Security.Claims;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Infrastructure.Identity;

namespace ShutkiVorta.Web.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public string? Email => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.Email) ?? Principal!.Identity!.Name : null;

    public string? Name => IsAuthenticated ? Principal!.FindFirstValue(AppClaimTypes.FullName) ?? Email : null;

    public bool IsAdmin => IsAuthenticated && Principal!.IsInRole(Roles.Admin);
}
