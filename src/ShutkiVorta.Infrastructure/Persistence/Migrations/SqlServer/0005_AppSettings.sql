-- Business settings managed in Admin → Settings (previously appsettings.json) (Microsoft SQL Server)

-- One row per configuration key, e.g. "Ordering:ClosedDays:0" = "Monday". Secrets are stored encrypted ("enc:v1:...").
CREATE TABLE dbo.AppSettings (
    [Key]         NVARCHAR(256)  NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
    Value         NVARCHAR(MAX)  NULL,
    UpdatedAtUtc  DATETIME2      NOT NULL,
    UpdatedBy     NVARCHAR(256)  NULL
);

-- One row per section; Revision is bumped on every save (detects concurrent edits and tells other servers to reload).
CREATE TABLE dbo.AppSettingsSections (
    Section       NVARCHAR(64)   NOT NULL CONSTRAINT PK_AppSettingsSections PRIMARY KEY,
    Revision      INT            NOT NULL,
    UpdatedAtUtc  DATETIME2      NOT NULL,
    UpdatedBy     NVARCHAR(256)  NULL
);
