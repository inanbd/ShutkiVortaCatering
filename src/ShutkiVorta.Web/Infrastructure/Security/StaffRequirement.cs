using Microsoft.AspNetCore.Authorization;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Web.Infrastructure.Security;

/// <summary>The /Admin folder's policy requirement: the signed-in person has staff access (read from the database).</summary>
public sealed class StaffRequirement : IAuthorizationRequirement;

/// <summary>
/// Scoped (it uses the per-request <see cref="ICurrentStaff"/>). Anonymous visitors are sent to sign in by the policy's
/// authenticated-user requirement; signed-in people without staff access get the access-denied page.
/// </summary>
public sealed class StaffRequirementHandler(ICurrentStaff currentStaff) : AuthorizationHandler<StaffRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if ((await currentStaff.GetAsync()).IsStaff)
        {
            context.Succeed(requirement);
        }
    }
}
