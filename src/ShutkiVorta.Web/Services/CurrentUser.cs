using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Infrastructure.Identity;

namespace ShutkiVorta.Web.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, IOptions<IdentityOptions> identityOptions) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public string? Email => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.Email) ?? Principal!.Identity!.Name : null;

    public string? Name => IsAuthenticated ? Principal!.FindFirstValue(AppClaimTypes.FullName) ?? Email : null;

    public string? SecurityStamp => IsAuthenticated ? Principal!.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType) : null;
}
