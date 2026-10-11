using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;

namespace ShutkiVorta.Web.Pages.Admin.Users;

/// <summary>Paths, labels and status pills for the Users and Permissions pages.</summary>
public static class StaffUi
{
    public const string UsersPath = "/admin/users";
    public const string AddPath = "/admin/users/add";
    public const string PermissionsPath = "/admin/permissions";
    public const string EmailSettingsPath = "/admin/settings/email";
    public const string WebsiteSettingsPath = "/admin/settings/website";

    public static string UserPath(string userId) => $"{UsersPath}/{Uri.EscapeDataString(userId)}";

    /// <summary>The person's shifts on the time clock's Team page.</summary>
    public static string ShiftsPath(string userId) => $"/admin/time-clock/team?user={Uri.EscapeDataString(userId)}";

    public static string PermissionsLabel(StaffMemberDto member) => member.Role switch
    {
        null => "—",
        StaffRole.Admin => "Everything",
        _ => member.UsesCustomPermissions ? "Custom" : "Role defaults",
    };

    /// <summary>What stands out about a person: on the clock, invitation pending, locked out… (text, pill class).</summary>
    public static IReadOnlyList<(string Text, string Css)> Statuses(StaffMemberDto member, DateTime? onClockSinceLocal, DateTime nowLocal)
    {
        var statuses = new List<(string, string)>();
        if (onClockSinceLocal is { } since)
        {
            statuses.Add(($"On the clock since {TimeClockText.When(since, nowLocal)}", "pill-on"));
        }

        if (!member.HasPassword)
        {
            statuses.Add(("Invitation pending", "pill-warn"));
        }
        else if (!member.EmailConfirmed)
        {
            statuses.Add(("Email not confirmed", "pill-warn"));
        }

        if (member.IsLockedOut)
        {
            statuses.Add(("Locked out", "pill-bad"));
        }

        if (member.HasConflictingRoles)
        {
            statuses.Add(("Several roles: save a role to fix", "pill-bad"));
        }

        return statuses;
    }

    /// <summary>How an invitation email got on, in words an owner understands.</summary>
    public static string EmailStatusText(StaffEmailStatusDto email) => email.Status switch
    {
        EmailStatus.Sent => "Sent",
        EmailStatus.SavedToFolder => "Saved to a folder on the server, not sent: email isn't set up yet",
        EmailStatus.Failed => email.LastError is { Length: > 0 } error ? $"Failed: {error}" : "Failed",
        EmailStatus.Disabled => "Not sent: email is turned off",
        _ => "Waiting to send",
    };

    public static string EmailStatusCss(EmailStatus status) => status switch
    {
        EmailStatus.Sent => "pill-on",
        EmailStatus.SavedToFolder or EmailStatus.Disabled => "pill-warn",
        EmailStatus.Failed => "pill-bad",
        _ => "pill-off",
    };

    /// <summary>An option of a module's select, e.g. "View &amp; change (default)".</summary>
    public static string LevelLabel(PermissionModule module, PermissionLevel level, StaffRole? defaultsOf) =>
        defaultsOf is { } role && module.DefaultLevel(role).Key == level.Key ? $"{level.Label} (default)" : level.Label;

    /// <summary>The modules a set of permissions opens, e.g. ("Orders", "View &amp; change"), leaving out "No access".</summary>
    public static IReadOnlyList<(string Module, string Level)> Summary(IReadOnlySet<string> permissions) =>
        PermissionCatalog.Modules
            .Select(m => (Module: m, Level: PermissionCatalog.LevelFor(m, permissions)))
            .Where(x => x.Level.Key != PermissionCatalog.NoAccess)
            .Select(x => (x.Module.Name, x.Level.Label))
            .ToList();
}

/// <summary>
/// The set-password link of an invitation just sent, carried to the person's page in TempData so it is shown exactly once
/// (a reload no longer has it). It is never stored on the server, logged or put in a URL.
/// </summary>
public sealed record InvitationFlash(string UserId, StaffInvitationKind Kind, string? Link, bool Emailed, DateTime CreatedAtUtc, DateTime ExpiresAtUtc)
{
    private const string Key = "StaffInvitation";

    public static void Save(ITempDataDictionary tempData, StaffInvitationDto invitation) =>
        tempData[Key] = JsonSerializer.Serialize(new InvitationFlash(
            invitation.UserId, invitation.Kind, invitation.Link, invitation.Emailed, invitation.CreatedAtUtc, invitation.ExpiresAtUtc));

    /// <summary>The invitation for this person, if one was just sent (reading it removes it).</summary>
    public static InvitationFlash? Take(ITempDataDictionary tempData, string userId) =>
        tempData[Key] is string json && JsonSerializer.Deserialize<InvitationFlash>(json) is { } flash
            && string.Equals(flash.UserId, userId, StringComparison.OrdinalIgnoreCase)
            ? flash
            : null;
}
