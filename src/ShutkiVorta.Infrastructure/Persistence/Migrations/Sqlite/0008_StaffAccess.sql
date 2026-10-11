-- Staff access: per-role permission overrides, staff profiles, per-user custom permissions and an audit log (SQLite)

-- One row per configurable role once an admin has saved its permissions; Revision guards against stale saves.
CREATE TABLE RolePermissionSets (
    Role          TEXT    NOT NULL PRIMARY KEY CHECK (Role IN ('Manager', 'Employee')),
    Revision      INTEGER NOT NULL DEFAULT 0,
    UpdatedAtUtc  TEXT    NOT NULL,
    UpdatedBy     TEXT    NOT NULL
);

-- An admin's choices per role; a permission without a row keeps the catalog default.
CREATE TABLE RolePermissions (
    Role        TEXT    NOT NULL CHECK (Role IN ('Manager', 'Employee')),
    Permission  TEXT    NOT NULL,
    IsGranted   INTEGER NOT NULL,
    PRIMARY KEY (Role, Permission)
);

-- Created when someone joins the staff and kept when they leave (former staff can be re-added).
CREATE TABLE StaffProfiles (
    UserId                 TEXT    NOT NULL PRIMARY KEY REFERENCES Users (Id) ON DELETE CASCADE,
    JobTitle               TEXT    NULL,
    UsesCustomPermissions  INTEGER NOT NULL DEFAULT 0,
    Version                INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc           TEXT    NOT NULL,
    UpdatedAtUtc           TEXT    NOT NULL,
    UpdatedBy              TEXT    NOT NULL
);

-- A person's own permissions, used instead of their role's while StaffProfiles.UsesCustomPermissions is set.
CREATE TABLE UserPermissions (
    UserId      TEXT NOT NULL REFERENCES Users (Id) ON DELETE CASCADE,
    Permission  TEXT NOT NULL,
    PRIMARY KEY (UserId, Permission)
);

-- No foreign keys: the audit trail outlives the accounts it mentions.
CREATE TABLE StaffAuditLog (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TargetUserId  TEXT    NULL,
    TargetName    TEXT    NULL,
    Action        TEXT    NOT NULL,
    Details       TEXT    NULL,
    ChangedBy     TEXT    NOT NULL,
    ChangedAtUtc  TEXT    NOT NULL
);
CREATE INDEX IX_StaffAuditLog_TargetUserId ON StaffAuditLog (TargetUserId, ChangedAtUtc);
CREATE INDEX IX_StaffAuditLog_ChangedAtUtc ON StaffAuditLog (ChangedAtUtc);

-- Administrators appointed before staff access required a confirmed email keep their access.
UPDATE Users SET EmailConfirmed = 1
 WHERE Id IN (SELECT ur.UserId FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'ADMIN');
