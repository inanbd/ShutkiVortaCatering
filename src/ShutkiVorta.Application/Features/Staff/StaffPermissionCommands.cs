using FluentValidation;
using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Staff;

// Roles and permissions: Admins only. Every change goes through IStaffPermissionStore, which holds the rules (not your own
// role, always an Admin who can sign in, custom permissions reset on a role change) and writes the audit entry.

/// <summary>Makes a staff member Admin, Manager or Employee. Their custom permissions are reset to the new role's.</summary>
/// <param name="Version">The person's StaffProfiles.Version as the page showed it.</param>
public sealed record ChangeStaffRoleCommand(string UserId, StaffRole Role, int Version) : IRequest, IRequireAdmin;

/// <summary>
/// Gives a Manager or Employee their own permissions, one level per module (module key → level key), or makes them follow
/// their role again when <see cref="Levels"/> is null.
/// </summary>
public sealed record SetStaffPermissionsCommand(string UserId, IReadOnlyDictionary<string, string>? Levels, int Version) : IRequest, IRequireAdmin;

/// <summary>What everyone with the role may do (except people with custom permissions), one level per module.</summary>
/// <param name="Revision">The role's permission revision as the page showed it.</param>
public sealed record SaveRolePermissionsCommand(StaffRole Role, IReadOnlyDictionary<string, string> Levels, int Revision) : IRequest, IRequireAdmin;

/// <summary>Removes an admin's changes to a role's permissions, so it has the defaults again.</summary>
public sealed record RestoreRoleDefaultsCommand(StaffRole Role, int Revision) : IRequest, IRequireAdmin;

internal static class PermissionInputRules
{
    public const string UnknownLevel = "Choose one of the listed levels for every module.";

    public static void ConfigurableRole<T>(this IRuleBuilder<T, StaffRole> rule) =>
        rule.Must(r => StaffRoles.Configurable.Contains(r)).WithMessage("Admins always have every permission.");
}

public sealed class ChangeStaffRoleCommandValidator : AbstractValidator<ChangeStaffRoleCommand>
{
    public ChangeStaffRoleCommandValidator() => RuleFor(x => x.Role).ValidRole();
}

public sealed class SetStaffPermissionsCommandValidator : AbstractValidator<SetStaffPermissionsCommand>
{
    public SetStaffPermissionsCommandValidator() =>
        RuleFor(x => x.Levels).Must(l => l is null || PermissionLevels.AreKnown(l)).WithMessage(PermissionInputRules.UnknownLevel);
}

public sealed class SaveRolePermissionsCommandValidator : AbstractValidator<SaveRolePermissionsCommand>
{
    public SaveRolePermissionsCommandValidator()
    {
        RuleFor(x => x.Role).ConfigurableRole();
        RuleFor(x => x.Levels).Must(PermissionLevels.AreKnown).WithMessage(PermissionInputRules.UnknownLevel);
    }
}

public sealed class RestoreRoleDefaultsCommandValidator : AbstractValidator<RestoreRoleDefaultsCommand>
{
    public RestoreRoleDefaultsCommandValidator() => RuleFor(x => x.Role).ConfigurableRole();
}

internal sealed class StaffPermissionCommandHandlers(
    IStaffDirectory directory,
    IStaffPermissionStore store,
    ICurrentUser currentUser) :
    IRequestHandler<ChangeStaffRoleCommand>,
    IRequestHandler<SetStaffPermissionsCommand>,
    IRequestHandler<SaveRolePermissionsCommand>,
    IRequestHandler<RestoreRoleDefaultsCommand>
{
    private string By => currentUser.Email ?? "Admin";

    public async Task Handle(ChangeStaffRoleCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        if (member.Role is not { } current)
        {
            throw new ValidationException($"{member.FullName} is not on the staff. Add them again instead.");
        }

        if (current == request.Role && !member.HasConflictingRoles)
        {
            throw new ValidationException($"{member.FullName} is already {request.Role.DisplayName()}.");
        }

        await StaffVersions.CheckAsync(store, member.UserId, request.Version, cancellationToken);
        var actor = currentUser.UserId ?? throw new ForbiddenAccessException();
        Check(await store.SetStaffRoleAsync(member.UserId, request.Role, actor, By, cancellationToken));
    }

    public async Task Handle(SetStaffPermissionsCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        if (member.Role is not ({ } role and (StaffRole.Manager or StaffRole.Employee)))
        {
            throw new ValidationException(member.Role is null
                ? $"{member.FullName} is not on the staff."
                : "Admins always have every permission.");
        }

        IReadOnlySet<string>? granted = null;
        if (request.Levels is not null)
        {
            // A module the form did not send keeps the level the person has now.
            var custom = await store.GetUserPermissionsAsync(member.UserId, cancellationToken);
            var current = custom.UsesCustomPermissions ? custom.Granted : (await store.GetRolePermissionsAsync(role, cancellationToken)).Granted;
            granted = PermissionLevels.Grants(request.Levels, current);
        }

        Check(await store.SetUserPermissionsAsync(member.UserId, granted, request.Version, By, cancellationToken));
    }

    public async Task Handle(SaveRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var current = await store.GetRolePermissionsAsync(request.Role, cancellationToken);
        var granted = PermissionLevels.Grants(request.Levels, current.Granted);
        Check(await store.SaveRolePermissionsAsync(request.Role, granted, request.Revision, By, cancellationToken));
    }

    public async Task Handle(RestoreRoleDefaultsCommand request, CancellationToken cancellationToken) =>
        Check(await store.RestoreRoleDefaultsAsync(request.Role, request.Revision, By, cancellationToken));

    private async Task<StaffMemberDto> RequireMemberAsync(string userId, CancellationToken cancellationToken) =>
        await directory.GetAsync(userId.Trim(), cancellationToken) ?? throw new NotFoundException("Staff member", userId);

    private static void Check(Common.Models.Result result)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException(string.Join(" ", result.Errors));
        }
    }
}
