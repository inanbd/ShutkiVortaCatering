-- Time clock: one row per shift, with the time and the device-reported location of each clock-in and clock-out (SQLite)
-- Coordinates are REAL, never NUMERIC: SQLite would store whole numbers as INTEGER, which Dapper cannot mix with doubles.
-- Location statuses and clock-out sources are the integer values of Domain.TimeClock.LocationStatus and ClockOutSource.

CREATE TABLE TimeClockShifts (
    Id                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId                   TEXT    NOT NULL REFERENCES Users (Id),
    EmployeeName             TEXT    NOT NULL,
    ClockInUtc               TEXT    NOT NULL,
    ClockInLatitude          REAL    NULL,
    ClockInLongitude         REAL    NULL,
    ClockInAccuracyMeters    REAL    NULL,
    ClockInDistanceMeters    REAL    NULL,
    ClockInLocationStatus    INTEGER NOT NULL,
    ClockInNote              TEXT    NULL,
    ClockOutUtc              TEXT    NULL,
    ClockOutLatitude         REAL    NULL,
    ClockOutLongitude        REAL    NULL,
    ClockOutAccuracyMeters   REAL    NULL,
    ClockOutDistanceMeters   REAL    NULL,
    ClockOutLocationStatus   INTEGER NULL,
    ClockOutNote             TEXT    NULL,
    ClockOutSource           INTEGER NULL,
    IsManualEntry            INTEGER NOT NULL DEFAULT 0,
    NeedsReview              INTEGER NOT NULL DEFAULT 0,
    OriginalClockInUtc       TEXT    NULL,
    OriginalClockOutUtc      TEXT    NULL,
    EditedBy                 TEXT    NULL,
    EditedAtUtc              TEXT    NULL,
    EditReason               TEXT    NULL,
    VoidedBy                 TEXT    NULL,
    VoidedAtUtc              TEXT    NULL,
    VoidReason               TEXT    NULL,
    Version                  INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc             TEXT    NOT NULL,
    UpdatedAtUtc             TEXT    NOT NULL,
    CHECK (ClockOutUtc IS NULL OR ClockOutUtc >= ClockInUtc)
);
-- At most one open shift per person; a second clock-in fails here even if two taps race.
CREATE UNIQUE INDEX IX_TimeClockShifts_OpenShift ON TimeClockShifts (UserId) WHERE ClockOutUtc IS NULL;
CREATE INDEX IX_TimeClockShifts_UserId_ClockInUtc ON TimeClockShifts (UserId, ClockInUtc);
CREATE INDEX IX_TimeClockShifts_ClockInUtc ON TimeClockShifts (ClockInUtc);
