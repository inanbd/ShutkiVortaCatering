-- Business settings managed in Admin → Settings (previously appsettings.json) (SQLite)

-- One row per configuration key, e.g. "Ordering:ClosedDays:0" = "Monday". Secrets are stored encrypted ("enc:v1:...").
CREATE TABLE AppSettings (
    [Key]         TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
    Value         TEXT NULL,
    UpdatedAtUtc  TEXT NOT NULL,
    UpdatedBy     TEXT NULL
);

-- One row per section; Revision is bumped on every save (detects concurrent edits and tells other servers to reload).
CREATE TABLE AppSettingsSections (
    Section       TEXT    NOT NULL PRIMARY KEY COLLATE NOCASE,
    Revision      INTEGER NOT NULL,
    UpdatedAtUtc  TEXT    NOT NULL,
    UpdatedBy     TEXT    NULL
);
