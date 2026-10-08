using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Common.Behaviors;

/// <summary>Defence in depth: admin-only requests are rejected even if a page forgets its [Authorize] attribute.</summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IRequireAdmin && !currentUser.IsAdmin)
        {
            throw new ForbiddenAccessException();
        }

        if (request is IRequireAuthenticatedUser && !currentUser.IsAuthenticated)
        {
            throw new ForbiddenAccessException("Please sign in to continue.");
        }

        return next(cancellationToken);
    }
}
