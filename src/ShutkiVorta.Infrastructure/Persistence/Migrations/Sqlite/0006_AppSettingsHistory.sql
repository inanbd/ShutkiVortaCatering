-- Settings change history, upgrade review flags and one-time re-import tracking (SQLite)

ALTER TABLE AppSettingsSections ADD COLUMN NeedsReview INTEGER NOT NULL DEFAULT 0;
ALTER TABLE AppSettingsSections ADD COLUMN ImportHash TEXT NULL;

CREATE TABLE AppSettingsHistory (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Section       TEXT    NOT NULL COLLATE NOCASE,
    [Key]         TEXT    NOT NULL,
    OldValue      TEXT    NULL,
    NewValue      TEXT    NULL,
    ChangedBy     TEXT    NOT NULL,
    ChangedAtUtc  TEXT    NOT NULL
);
CREATE INDEX IX_AppSettingsHistory_Section ON AppSettingsHistory (Section, ChangedAtUtc);
