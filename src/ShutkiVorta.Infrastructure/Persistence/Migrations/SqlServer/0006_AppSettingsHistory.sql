-- Settings change history, upgrade review flags and one-time re-import tracking (Microsoft SQL Server)

ALTER TABLE dbo.AppSettingsSections ADD
    NeedsReview  BIT          NOT NULL CONSTRAINT DF_AppSettingsSections_NeedsReview DEFAULT 0,
    ImportHash   NVARCHAR(64) NULL;

CREATE TABLE dbo.AppSettingsHistory (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppSettingsHistory PRIMARY KEY,
    Section       NVARCHAR(64)    NOT NULL,
    [Key]         NVARCHAR(256)   NOT NULL,
    OldValue      NVARCHAR(4000)  NULL,
    NewValue      NVARCHAR(4000)  NULL,
    ChangedBy     NVARCHAR(256)   NOT NULL,
    ChangedAtUtc  DATETIME2       NOT NULL
);
CREATE INDEX IX_AppSettingsHistory_Section ON dbo.AppSettingsHistory (Section, ChangedAtUtc);
