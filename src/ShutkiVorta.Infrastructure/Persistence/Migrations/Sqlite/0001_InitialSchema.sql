-- Shutki Vorta Catering: initial schema (SQLite)

CREATE TABLE Users (
    Id                    TEXT    NOT NULL PRIMARY KEY,
    UserName              TEXT    NOT NULL,
    NormalizedUserName    TEXT    NOT NULL,
    Email                 TEXT    NOT NULL,
    NormalizedEmail       TEXT    NOT NULL,
    EmailConfirmed        INTEGER NOT NULL DEFAULT 0,
    PasswordHash          TEXT    NULL,
    SecurityStamp         TEXT    NULL,
    ConcurrencyStamp      TEXT    NULL,
    PhoneNumber           TEXT    NULL,
    PhoneNumberConfirmed  INTEGER NOT NULL DEFAULT 0,
    LockoutEnabled        INTEGER NOT NULL DEFAULT 1,
    LockoutEndUtc         TEXT    NULL,
    AccessFailedCount     INTEGER NOT NULL DEFAULT 0,
    FullName              TEXT    NOT NULL,
    AddressLine1          TEXT    NULL,
    AddressLine2          TEXT    NULL,
    City                  TEXT    NULL,
    State                 TEXT    NULL,
    PostalCode            TEXT    NULL,
    CreatedAtUtc          TEXT    NOT NULL
);
CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users (NormalizedUserName);
CREATE UNIQUE INDEX IX_Users_NormalizedEmail ON Users (NormalizedEmail);

CREATE TABLE Roles (
    Id                TEXT NOT NULL PRIMARY KEY,
    Name              TEXT NOT NULL,
    NormalizedName    TEXT NOT NULL,
    ConcurrencyStamp  TEXT NULL
);
CREATE UNIQUE INDEX IX_Roles_NormalizedName ON Roles (NormalizedName);

CREATE TABLE UserRoles (
    UserId TEXT NOT NULL REFERENCES Users (Id) ON DELETE CASCADE,
    RoleId TEXT NOT NULL REFERENCES Roles (Id) ON DELETE CASCADE,
    PRIMARY KEY (UserId, RoleId)
);
CREATE INDEX IX_UserRoles_RoleId ON UserRoles (RoleId);

CREATE TABLE MenuItems (
    Id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name              TEXT    NOT NULL,
    BengaliName       TEXT    NULL,
    Slug              TEXT    NOT NULL,
    Category          INTEGER NOT NULL,
    ShortDescription  TEXT    NOT NULL,
    Description       TEXT    NOT NULL,
    Ingredients       TEXT    NULL,
    PricePerUnit      NUMERIC NOT NULL,
    Unit              TEXT    NOT NULL,
    MinimumQuantity   NUMERIC NOT NULL,
    QuantityStep      NUMERIC NOT NULL,
    SpiceLevel        INTEGER NOT NULL,
    ImageUrl          TEXT    NULL,
    ImageAlt          TEXT    NULL,
    ImageCredit       TEXT    NULL,
    IsAvailable       INTEGER NOT NULL,
    IsFeatured        INTEGER NOT NULL,
    SortOrder         INTEGER NOT NULL,
    MetaTitle         TEXT    NULL,
    MetaDescription   TEXT    NULL,
    CreatedAtUtc      TEXT    NOT NULL,
    UpdatedAtUtc      TEXT    NOT NULL
);
CREATE UNIQUE INDEX IX_MenuItems_Slug ON MenuItems (Slug);

CREATE TABLE Orders (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderNumber    TEXT    NOT NULL,
    TrackingToken  TEXT    NOT NULL,
    CustomerId     TEXT    NULL REFERENCES Users (Id) ON DELETE SET NULL,
    CustomerName   TEXT    NOT NULL,
    Email          TEXT    NOT NULL,
    Phone          TEXT    NOT NULL,
    Fulfillment    INTEGER NOT NULL,
    AddressLine1   TEXT    NULL,
    AddressLine2   TEXT    NULL,
    City           TEXT    NULL,
    State          TEXT    NULL,
    PostalCode     TEXT    NULL,
    ScheduledFor   TEXT    NOT NULL,
    CustomerNotes  TEXT    NULL,
    AdminNotes     TEXT    NULL,
    Status         INTEGER NOT NULL,
    Subtotal       NUMERIC NOT NULL,
    DeliveryFee    NUMERIC NOT NULL,
    Tax            NUMERIC NOT NULL,
    Total          NUMERIC NOT NULL,
    CreatedAtUtc   TEXT    NOT NULL,
    UpdatedAtUtc   TEXT    NOT NULL
);
CREATE UNIQUE INDEX IX_Orders_OrderNumber ON Orders (OrderNumber);
CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);
CREATE INDEX IX_Orders_Email ON Orders (Email);
CREATE INDEX IX_Orders_Status ON Orders (Status);
CREATE INDEX IX_Orders_ScheduledFor ON Orders (ScheduledFor);
CREATE INDEX IX_Orders_CreatedAtUtc ON Orders (CreatedAtUtc);

CREATE TABLE OrderLines (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId          INTEGER NOT NULL REFERENCES Orders (Id) ON DELETE CASCADE,
    MenuItemId       INTEGER NOT NULL REFERENCES MenuItems (Id),
    ItemName         TEXT    NOT NULL,
    ItemBengaliName  TEXT    NULL,
    Unit             TEXT    NOT NULL,
    UnitPrice        NUMERIC NOT NULL,
    Quantity         NUMERIC NOT NULL,
    LineTotal        NUMERIC NOT NULL
);
CREATE INDEX IX_OrderLines_OrderId ON OrderLines (OrderId);
CREATE INDEX IX_OrderLines_MenuItemId ON OrderLines (MenuItemId);

CREATE TABLE OrderStatusChanges (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId       INTEGER NOT NULL REFERENCES Orders (Id) ON DELETE CASCADE,
    Status        INTEGER NOT NULL,
    Note          TEXT    NULL,
    ChangedBy     TEXT    NOT NULL,
    ChangedAtUtc  TEXT    NOT NULL
);
CREATE INDEX IX_OrderStatusChanges_OrderId ON OrderStatusChanges (OrderId);

CREATE TABLE CateringInquiries (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name          TEXT    NOT NULL,
    Email         TEXT    NOT NULL,
    Phone         TEXT    NULL,
    EventDate     TEXT    NULL,
    GuestCount    INTEGER NULL,
    Message       TEXT    NOT NULL,
    IsHandled     INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc  TEXT    NOT NULL,
    HandledAtUtc  TEXT    NULL
);
CREATE INDEX IX_CateringInquiries_IsHandled ON CateringInquiries (IsHandled);
