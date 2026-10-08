-- Shutki Vorta Catering: initial schema (Microsoft SQL Server)

CREATE TABLE dbo.Users (
    Id                    NVARCHAR(450)  NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    UserName              NVARCHAR(256)  NOT NULL,
    NormalizedUserName    NVARCHAR(256)  NOT NULL,
    Email                 NVARCHAR(256)  NOT NULL,
    NormalizedEmail       NVARCHAR(256)  NOT NULL,
    EmailConfirmed        BIT            NOT NULL CONSTRAINT DF_Users_EmailConfirmed DEFAULT 0,
    PasswordHash          NVARCHAR(MAX)  NULL,
    SecurityStamp         NVARCHAR(MAX)  NULL,
    ConcurrencyStamp      NVARCHAR(MAX)  NULL,
    PhoneNumber           NVARCHAR(32)   NULL,
    PhoneNumberConfirmed  BIT            NOT NULL CONSTRAINT DF_Users_PhoneNumberConfirmed DEFAULT 0,
    LockoutEnabled        BIT            NOT NULL CONSTRAINT DF_Users_LockoutEnabled DEFAULT 1,
    LockoutEndUtc         DATETIME2      NULL,
    AccessFailedCount     INT            NOT NULL CONSTRAINT DF_Users_AccessFailedCount DEFAULT 0,
    FullName              NVARCHAR(120)  NOT NULL,
    AddressLine1          NVARCHAR(200)  NULL,
    AddressLine2          NVARCHAR(200)  NULL,
    City                  NVARCHAR(100)  NULL,
    State                 NVARCHAR(50)   NULL,
    PostalCode            NVARCHAR(20)   NULL,
    CreatedAtUtc          DATETIME2      NOT NULL
);
CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON dbo.Users (NormalizedUserName);
CREATE UNIQUE INDEX IX_Users_NormalizedEmail ON dbo.Users (NormalizedEmail);

CREATE TABLE dbo.Roles (
    Id                NVARCHAR(450) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name              NVARCHAR(256) NOT NULL,
    NormalizedName    NVARCHAR(256) NOT NULL,
    ConcurrencyStamp  NVARCHAR(MAX) NULL
);
CREATE UNIQUE INDEX IX_Roles_NormalizedName ON dbo.Roles (NormalizedName);

CREATE TABLE dbo.UserRoles (
    UserId NVARCHAR(450) NOT NULL CONSTRAINT FK_UserRoles_Users REFERENCES dbo.Users (Id) ON DELETE CASCADE,
    RoleId NVARCHAR(450) NOT NULL CONSTRAINT FK_UserRoles_Roles REFERENCES dbo.Roles (Id) ON DELETE CASCADE,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId)
);
CREATE INDEX IX_UserRoles_RoleId ON dbo.UserRoles (RoleId);

CREATE TABLE dbo.MenuItems (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MenuItems PRIMARY KEY,
    Name              NVARCHAR(120)   NOT NULL,
    BengaliName       NVARCHAR(120)   NULL,
    Slug              NVARCHAR(120)   NOT NULL,
    Category          INT             NOT NULL,
    ShortDescription  NVARCHAR(300)   NOT NULL,
    Description       NVARCHAR(4000)  NOT NULL,
    Ingredients       NVARCHAR(1000)  NULL,
    PricePerUnit      DECIMAL(10,2)   NOT NULL,
    Unit              NVARCHAR(20)    NOT NULL,
    MinimumQuantity   DECIMAL(10,2)   NOT NULL,
    QuantityStep      DECIMAL(10,2)   NOT NULL,
    SpiceLevel        INT             NOT NULL,
    ImageUrl          NVARCHAR(500)   NULL,
    ImageAlt          NVARCHAR(200)   NULL,
    ImageCredit       NVARCHAR(300)   NULL,
    IsAvailable       BIT             NOT NULL,
    IsFeatured        BIT             NOT NULL,
    SortOrder         INT             NOT NULL,
    MetaTitle         NVARCHAR(70)    NULL,
    MetaDescription   NVARCHAR(170)   NULL,
    CreatedAtUtc      DATETIME2       NOT NULL,
    UpdatedAtUtc      DATETIME2       NOT NULL
);
CREATE UNIQUE INDEX IX_MenuItems_Slug ON dbo.MenuItems (Slug);

CREATE TABLE dbo.Orders (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    OrderNumber    NVARCHAR(32)    NOT NULL,
    TrackingToken  NVARCHAR(64)    NOT NULL,
    CustomerId     NVARCHAR(450)   NULL CONSTRAINT FK_Orders_Users REFERENCES dbo.Users (Id) ON DELETE SET NULL,
    CustomerName   NVARCHAR(120)   NOT NULL,
    Email          NVARCHAR(256)   NOT NULL,
    Phone          NVARCHAR(32)    NOT NULL,
    Fulfillment    INT             NOT NULL,
    AddressLine1   NVARCHAR(200)   NULL,
    AddressLine2   NVARCHAR(200)   NULL,
    City           NVARCHAR(100)   NULL,
    State          NVARCHAR(50)    NULL,
    PostalCode     NVARCHAR(20)    NULL,
    ScheduledFor   DATETIME2       NOT NULL,
    CustomerNotes  NVARCHAR(1000)  NULL,
    AdminNotes     NVARCHAR(2000)  NULL,
    Status         INT             NOT NULL,
    Subtotal       DECIMAL(10,2)   NOT NULL,
    DeliveryFee    DECIMAL(10,2)   NOT NULL,
    Tax            DECIMAL(10,2)   NOT NULL,
    Total          DECIMAL(10,2)   NOT NULL,
    CreatedAtUtc   DATETIME2       NOT NULL,
    UpdatedAtUtc   DATETIME2       NOT NULL
);
CREATE UNIQUE INDEX IX_Orders_OrderNumber ON dbo.Orders (OrderNumber);
CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId);
CREATE INDEX IX_Orders_Email ON dbo.Orders (Email);
CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);
CREATE INDEX IX_Orders_ScheduledFor ON dbo.Orders (ScheduledFor);
CREATE INDEX IX_Orders_CreatedAtUtc ON dbo.Orders (CreatedAtUtc);

CREATE TABLE dbo.OrderLines (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderLines PRIMARY KEY,
    OrderId          INT            NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders (Id) ON DELETE CASCADE,
    MenuItemId       INT            NOT NULL CONSTRAINT FK_OrderLines_MenuItems REFERENCES dbo.MenuItems (Id),
    ItemName         NVARCHAR(120)  NOT NULL,
    ItemBengaliName  NVARCHAR(120)  NULL,
    Unit             NVARCHAR(20)   NOT NULL,
    UnitPrice        DECIMAL(10,2)  NOT NULL,
    Quantity         DECIMAL(10,2)  NOT NULL,
    LineTotal        DECIMAL(10,2)  NOT NULL
);
CREATE INDEX IX_OrderLines_OrderId ON dbo.OrderLines (OrderId);
CREATE INDEX IX_OrderLines_MenuItemId ON dbo.OrderLines (MenuItemId);

CREATE TABLE dbo.OrderStatusChanges (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderStatusChanges PRIMARY KEY,
    OrderId       INT            NOT NULL CONSTRAINT FK_OrderStatusChanges_Orders REFERENCES dbo.Orders (Id) ON DELETE CASCADE,
    Status        INT            NOT NULL,
    Note          NVARCHAR(500)  NULL,
    ChangedBy     NVARCHAR(256)  NOT NULL,
    ChangedAtUtc  DATETIME2      NOT NULL
);
CREATE INDEX IX_OrderStatusChanges_OrderId ON dbo.OrderStatusChanges (OrderId);

CREATE TABLE dbo.CateringInquiries (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CateringInquiries PRIMARY KEY,
    Name          NVARCHAR(120)   NOT NULL,
    Email         NVARCHAR(256)   NOT NULL,
    Phone         NVARCHAR(32)    NULL,
    EventDate     DATETIME2       NULL,
    GuestCount    INT             NULL,
    Message       NVARCHAR(4000)  NOT NULL,
    IsHandled     BIT             NOT NULL CONSTRAINT DF_CateringInquiries_IsHandled DEFAULT 0,
    CreatedAtUtc  DATETIME2       NOT NULL,
    HandledAtUtc  DATETIME2       NULL
);
CREATE INDEX IX_CateringInquiries_IsHandled ON dbo.CateringInquiries (IsHandled);
