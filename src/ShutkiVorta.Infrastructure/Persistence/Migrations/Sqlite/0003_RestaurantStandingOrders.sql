-- Restaurant standing (recurring) orders, wholesale prices and inquiry topics (SQLite)

CREATE TABLE StandingOrders (
    Id                    INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Reference             TEXT    NOT NULL,
    CustomerId            TEXT    NULL REFERENCES Users (Id) ON DELETE SET NULL,
    BusinessName          TEXT    NOT NULL,
    ContactName           TEXT    NOT NULL,
    Email                 TEXT    NOT NULL,
    Phone                 TEXT    NOT NULL,
    TaxPermitNumber       TEXT    NULL,
    Fulfillment           INTEGER NOT NULL,
    AddressLine1          TEXT    NULL,
    AddressLine2          TEXT    NULL,
    City                  TEXT    NULL,
    State                 TEXT    NULL,
    PostalCode            TEXT    NULL,
    DaysOfWeek            INTEGER NOT NULL,
    PreferredTimeMinutes  INTEGER NOT NULL,
    StartDate             TEXT    NOT NULL,
    EndDate               TEXT    NULL,
    Notes                 TEXT    NULL,
    AdminNotes            TEXT    NULL,
    Status                INTEGER NOT NULL,
    StatusReason          TEXT    NULL,
    TaxExempt             INTEGER NOT NULL DEFAULT 0,
    DeliveryFee           NUMERIC NOT NULL DEFAULT 0,
    CreatedAtUtc          TEXT    NOT NULL,
    UpdatedAtUtc          TEXT    NOT NULL,
    ApprovedAtUtc         TEXT    NULL
);
CREATE UNIQUE INDEX IX_StandingOrders_Reference ON StandingOrders (Reference);
CREATE INDEX IX_StandingOrders_CustomerId ON StandingOrders (CustomerId);
CREATE INDEX IX_StandingOrders_Status ON StandingOrders (Status);

CREATE TABLE StandingOrderLines (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    StandingOrderId  INTEGER NOT NULL REFERENCES StandingOrders (Id) ON DELETE CASCADE,
    MenuItemId       INTEGER NOT NULL REFERENCES MenuItems (Id),
    ItemName         TEXT    NOT NULL,
    ItemBengaliName  TEXT    NULL,
    Unit             TEXT    NOT NULL,
    UnitPrice        NUMERIC NOT NULL,
    Quantity         NUMERIC NOT NULL
);
CREATE INDEX IX_StandingOrderLines_StandingOrderId ON StandingOrderLines (StandingOrderId);
CREATE INDEX IX_StandingOrderLines_MenuItemId ON StandingOrderLines (MenuItemId);

CREATE TABLE StandingOrderEvents (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    StandingOrderId  INTEGER NOT NULL REFERENCES StandingOrders (Id) ON DELETE CASCADE,
    Description      TEXT    NOT NULL,
    ChangedBy        TEXT    NOT NULL,
    ChangedAtUtc     TEXT    NOT NULL
);
CREATE INDEX IX_StandingOrderEvents_StandingOrderId ON StandingOrderEvents (StandingOrderId);

ALTER TABLE Orders ADD COLUMN StandingOrderId INTEGER NULL REFERENCES StandingOrders (Id) ON DELETE SET NULL;
ALTER TABLE Orders ADD COLUMN CompanyName TEXT NULL;
CREATE INDEX IX_Orders_StandingOrderId ON Orders (StandingOrderId);

-- One row per standing order per delivery date: makes order generation idempotent and records skips.
CREATE TABLE StandingOrderOccurrences (
    StandingOrderId  INTEGER NOT NULL REFERENCES StandingOrders (Id) ON DELETE CASCADE,
    OccurrenceDate   TEXT    NOT NULL,
    Status           INTEGER NOT NULL,
    OrderId          INTEGER NULL REFERENCES Orders (Id) ON DELETE SET NULL,
    Reason           TEXT    NULL,
    CreatedAtUtc     TEXT    NOT NULL,
    PRIMARY KEY (StandingOrderId, OccurrenceDate)
);
CREATE INDEX IX_StandingOrderOccurrences_OrderId ON StandingOrderOccurrences (OrderId);

ALTER TABLE MenuItems ADD COLUMN WholesalePricePerUnit NUMERIC NULL;
ALTER TABLE CateringInquiries ADD COLUMN Topic INTEGER NOT NULL DEFAULT 1;
