-- Staff access: per-role permission overrides, staff profiles, per-user custom permissions and an audit log (Microsoft SQL Server)

-- One row per configurable role once an admin has saved its permissions; Revision guards against stale saves.
CREATE TABLE dbo.RolePermissionSets (
    Role          NVARCHAR(20)   NOT NULL CONSTRAINT PK_RolePermissionSets PRIMARY KEY
                                 CONSTRAINT CK_RolePermissionSets_Role CHECK (Role IN (N'Manager', N'Employee')),
    Revision      INT            NOT NULL CONSTRAINT DF_RolePermissionSets_Revision DEFAULT 0,
    UpdatedAtUtc  DATETIME2      NOT NULL,
    UpdatedBy     NVARCHAR(256)  NOT NULL
);

-- An admin's choices per role; a permission without a row keeps the catalog default.
CREATE TABLE dbo.RolePermissions (
    Role        NVARCHAR(20)  NOT NULL CONSTRAINT CK_RolePermissions_Role CHECK (Role IN (N'Manager', N'Employee')),
    Permission  NVARCHAR(64)  NOT NULL,
    IsGranted   BIT           NOT NULL CONSTRAINT DF_RolePermissions_IsGranted DEFAULT 0,
    CONSTRAINT PK_RolePermissions PRIMARY KEY (Role, Permission)
);

-- Created when someone joins the staff and kept when they leave (former staff can be re-added).
CREATE TABLE dbo.StaffProfiles (
    UserId                 NVARCHAR(450)  NOT NULL CONSTRAINT PK_StaffProfiles PRIMARY KEY
                                          CONSTRAINT FK_StaffProfiles_Users REFERENCES dbo.Users (Id) ON DELETE CASCADE,
    JobTitle               NVARCHAR(100)  NULL,
    UsesCustomPermissions  BIT            NOT NULL CONSTRAINT DF_StaffProfiles_UsesCustomPermissions DEFAULT 0,
    Version                INT            NOT NULL CONSTRAINT DF_StaffProfiles_Version DEFAULT 0,
    CreatedAtUtc           DATETIME2      NOT NULL,
    UpdatedAtUtc           DATETIME2      NOT NULL,
    UpdatedBy              NVARCHAR(256)  NOT NULL
);

-- A person's own permissions, used instead of their role's while StaffProfiles.UsesCustomPermissions is set.
-- (Only one cascading foreign key to Users per table, and none to StaffProfiles: SQL Server rejects multiple cascade paths.)
CREATE TABLE dbo.UserPermissions (
    UserId      NVARCHAR(450)  NOT NULL CONSTRAINT FK_UserPermissions_Users REFERENCES dbo.Users (Id) ON DELETE CASCADE,
    Permission  NVARCHAR(64)   NOT NULL,
    CONSTRAINT PK_UserPermissions PRIMARY KEY (UserId, Permission)
);

-- No foreign keys: the audit trail outlives the accounts it mentions.
CREATE TABLE dbo.StaffAuditLog (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StaffAuditLog PRIMARY KEY,
    TargetUserId  NVARCHAR(450)   NULL,
    TargetName    NVARCHAR(120)   NULL,
    Action        NVARCHAR(50)    NOT NULL,
    Details       NVARCHAR(2000)  NULL,
    ChangedBy     NVARCHAR(256)   NOT NULL,
    ChangedAtUtc  DATETIME2       NOT NULL
);
CREATE INDEX IX_StaffAuditLog_TargetUserId ON dbo.StaffAuditLog (TargetUserId, ChangedAtUtc);
CREATE INDEX IX_StaffAuditLog_ChangedAtUtc ON dbo.StaffAuditLog (ChangedAtUtc);

-- Administrators appointed before staff access required a confirmed email keep their access.
UPDATE dbo.Users SET EmailConfirmed = 1
 WHERE Id IN (SELECT ur.UserId FROM dbo.UserRoles ur INNER JOIN dbo.Roles r ON r.Id = ur.RoleId WHERE r.NormalizedName = N'ADMIN');
