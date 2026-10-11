-- Time clock: one row per shift, with the time and the device-reported location of each clock-in and clock-out (Microsoft SQL Server)
-- Location statuses and clock-out sources are the integer values of Domain.TimeClock.LocationStatus and ClockOutSource.

-- Shifts are kept when someone leaves the staff, so the foreign key to Users does not cascade.
CREATE TABLE dbo.TimeClockShifts (
    Id                       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TimeClockShifts PRIMARY KEY,
    UserId                   NVARCHAR(450)  NOT NULL CONSTRAINT FK_TimeClockShifts_Users REFERENCES dbo.Users (Id),
    EmployeeName             NVARCHAR(120)  NOT NULL,
    ClockInUtc               DATETIME2      NOT NULL,
    ClockInLatitude          FLOAT          NULL,
    ClockInLongitude         FLOAT          NULL,
    ClockInAccuracyMeters    FLOAT          NULL,
    ClockInDistanceMeters    FLOAT          NULL,
    ClockInLocationStatus    INT            NOT NULL,
    ClockInNote              NVARCHAR(500)  NULL,
    ClockOutUtc              DATETIME2      NULL,
    ClockOutLatitude         FLOAT          NULL,
    ClockOutLongitude        FLOAT          NULL,
    ClockOutAccuracyMeters   FLOAT          NULL,
    ClockOutDistanceMeters   FLOAT          NULL,
    ClockOutLocationStatus   INT            NULL,
    ClockOutNote             NVARCHAR(500)  NULL,
    ClockOutSource           INT            NULL,
    IsManualEntry            BIT            NOT NULL CONSTRAINT DF_TimeClockShifts_IsManualEntry DEFAULT 0,
    NeedsReview              BIT            NOT NULL CONSTRAINT DF_TimeClockShifts_NeedsReview DEFAULT 0,
    OriginalClockInUtc       DATETIME2      NULL,
    OriginalClockOutUtc      DATETIME2      NULL,
    EditedBy                 NVARCHAR(256)  NULL,
    EditedAtUtc              DATETIME2      NULL,
    EditReason               NVARCHAR(500)  NULL,
    VoidedBy                 NVARCHAR(256)  NULL,
    VoidedAtUtc              DATETIME2      NULL,
    VoidReason               NVARCHAR(500)  NULL,
    Version                  INT            NOT NULL CONSTRAINT DF_TimeClockShifts_Version DEFAULT 0,
    CreatedAtUtc             DATETIME2      NOT NULL,
    UpdatedAtUtc             DATETIME2      NOT NULL,
    CONSTRAINT CK_TimeClockShifts_Order CHECK (ClockOutUtc IS NULL OR ClockOutUtc >= ClockInUtc)
);
-- At most one open shift per person; a second clock-in fails here even if two taps race.
CREATE UNIQUE INDEX IX_TimeClockShifts_OpenShift ON dbo.TimeClockShifts (UserId) WHERE ClockOutUtc IS NULL;
CREATE INDEX IX_TimeClockShifts_UserId_ClockInUtc ON dbo.TimeClockShifts (UserId, ClockInUtc);
CREATE INDEX IX_TimeClockShifts_ClockInUtc ON dbo.TimeClockShifts (ClockInUtc);
