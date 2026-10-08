-- Restaurant standing (recurring) orders, wholesale prices and inquiry topics (Microsoft SQL Server)

CREATE TABLE dbo.StandingOrders (
    Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandingOrders PRIMARY KEY,
    Reference             NVARCHAR(32)    NOT NULL,
    CustomerId            NVARCHAR(450)   NULL CONSTRAINT FK_StandingOrders_Users REFERENCES dbo.Users (Id) ON DELETE SET NULL,
    BusinessName          NVARCHAR(150)   NOT NULL,
    ContactName           NVARCHAR(120)   NOT NULL,
    Email                 NVARCHAR(256)   NOT NULL,
    Phone                 NVARCHAR(32)    NOT NULL,
    TaxPermitNumber       NVARCHAR(32)    NULL,
    Fulfillment           INT             NOT NULL,
    AddressLine1          NVARCHAR(200)   NULL,
    AddressLine2          NVARCHAR(200)   NULL,
    City                  NVARCHAR(100)   NULL,
    State                 NVARCHAR(50)    NULL,
    PostalCode            NVARCHAR(20)    NULL,
    DaysOfWeek            INT             NOT NULL,
    PreferredTimeMinutes  INT             NOT NULL,
    StartDate             DATETIME2       NOT NULL,
    EndDate               DATETIME2       NULL,
    Notes                 NVARCHAR(1000)  NULL,
    AdminNotes            NVARCHAR(2000)  NULL,
    Status                INT             NOT NULL,
    StatusReason          NVARCHAR(500)   NULL,
    TaxExempt             BIT             NOT NULL CONSTRAINT DF_StandingOrders_TaxExempt DEFAULT 0,
    DeliveryFee           DECIMAL(10,2)   NOT NULL CONSTRAINT DF_StandingOrders_DeliveryFee DEFAULT 0,
    CreatedAtUtc          DATETIME2       NOT NULL,
    UpdatedAtUtc          DATETIME2       NOT NULL,
    ApprovedAtUtc         DATETIME2       NULL
);
CREATE UNIQUE INDEX IX_StandingOrders_Reference ON dbo.StandingOrders (Reference);
CREATE INDEX IX_StandingOrders_CustomerId ON dbo.StandingOrders (CustomerId);
CREATE INDEX IX_StandingOrders_Status ON dbo.StandingOrders (Status);

CREATE TABLE dbo.StandingOrderLines (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandingOrderLines PRIMARY KEY,
    StandingOrderId  INT            NOT NULL CONSTRAINT FK_StandingOrderLines_StandingOrders REFERENCES dbo.StandingOrders (Id) ON DELETE CASCADE,
    MenuItemId       INT            NOT NULL CONSTRAINT FK_StandingOrderLines_MenuItems REFERENCES dbo.MenuItems (Id),
    ItemName         NVARCHAR(120)  NOT NULL,
    ItemBengaliName  NVARCHAR(120)  NULL,
    Unit             NVARCHAR(20)   NOT NULL,
    UnitPrice        DECIMAL(10,2)  NOT NULL,
    Quantity         DECIMAL(10,2)  NOT NULL
);
CREATE INDEX IX_StandingOrderLines_StandingOrderId ON dbo.StandingOrderLines (StandingOrderId);
CREATE INDEX IX_StandingOrderLines_MenuItemId ON dbo.StandingOrderLines (MenuItemId);

CREATE TABLE dbo.StandingOrderEvents (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandingOrderEvents PRIMARY KEY,
    StandingOrderId  INT            NOT NULL CONSTRAINT FK_StandingOrderEvents_StandingOrders REFERENCES dbo.StandingOrders (Id) ON DELETE CASCADE,
    Description      NVARCHAR(500)  NOT NULL,
    ChangedBy        NVARCHAR(256)  NOT NULL,
    ChangedAtUtc     DATETIME2      NOT NULL
);
CREATE INDEX IX_StandingOrderEvents_StandingOrderId ON dbo.StandingOrderEvents (StandingOrderId);

-- Orders keep their history if a standing order row is ever removed, so no cascade here.
ALTER TABLE dbo.Orders ADD
    StandingOrderId  INT            NULL CONSTRAINT FK_Orders_StandingOrders REFERENCES dbo.StandingOrders (Id),
    CompanyName      NVARCHAR(150)  NULL;
GO
CREATE INDEX IX_Orders_StandingOrderId ON dbo.Orders (StandingOrderId);

-- One row per standing order per delivery date: makes order generation idempotent and records skips.
-- (No FK to Orders: SQL Server rejects the second cascade path; the scheduler keeps OrderId consistent.)
CREATE TABLE dbo.StandingOrderOccurrences (
    StandingOrderId  INT            NOT NULL CONSTRAINT FK_StandingOrderOccurrences_StandingOrders REFERENCES dbo.StandingOrders (Id) ON DELETE CASCADE,
    OccurrenceDate   DATETIME2      NOT NULL,
    Status           INT            NOT NULL,
    OrderId          INT            NULL,
    Reason           NVARCHAR(500)  NULL,
    CreatedAtUtc     DATETIME2      NOT NULL,
    CONSTRAINT PK_StandingOrderOccurrences PRIMARY KEY (StandingOrderId, OccurrenceDate)
);
CREATE INDEX IX_StandingOrderOccurrences_OrderId ON dbo.StandingOrderOccurrences (OrderId);

ALTER TABLE dbo.MenuItems ADD WholesalePricePerUnit DECIMAL(10,2) NULL;
ALTER TABLE dbo.CateringInquiries ADD Topic INT NOT NULL CONSTRAINT DF_CateringInquiries_Topic DEFAULT 1;
GO
CREATE INDEX IX_CateringInquiries_Topic ON dbo.CateringInquiries (Topic);
