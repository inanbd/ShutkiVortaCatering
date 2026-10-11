using System.Reflection;
using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Common.Behaviors;

/// <summary>
/// Defence in depth: every staff request is checked against the database-backed staff access, even if a page forgets its
/// attribute. Enforces <see cref="IRequireAuthenticatedUser"/>, <see cref="IRequireStaff"/>, <see cref="IRequireAdmin"/> and
/// <see cref="RequirePermissionAttribute"/>.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser, ICurrentStaff currentStaff)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Looked up once per request type: the attributes and interfaces of a type never change.
    private static readonly RequirePermissionAttribute? RequiredPermission = typeof(TRequest).GetCustomAttribute<RequirePermissionAttribute>(inherit: true);
    private static readonly bool RequiresAdmin = typeof(IRequireAdmin).IsAssignableFrom(typeof(TRequest));
    private static readonly bool RequiresStaff = typeof(IRequireStaff).IsAssignableFrom(typeof(TRequest));
    private static readonly bool RequiresUser = typeof(IRequireAuthenticatedUser).IsAssignableFrom(typeof(TRequest));

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (RequiresUser && !currentUser.IsAuthenticated)
        {
            throw new ForbiddenAccessException("Please sign in to continue.");
        }

        if (RequiresAdmin || RequiresStaff || RequiredPermission is not null)
        {
            var access = await currentStaff.GetAsync(cancellationToken);
            if (!access.IsStaff
                || (RequiresAdmin && !access.IsAdmin)
                || (RequiredPermission is not null && !access.Can(RequiredPermission.Permission)))
            {
                throw new ForbiddenAccessException();
            }
        }

        return await next(cancellationToken);
    }
}
