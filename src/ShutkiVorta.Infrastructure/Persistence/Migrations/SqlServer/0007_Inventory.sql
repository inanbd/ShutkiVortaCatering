-- Kitchen inventory: saved item names, purchases with their items, and receipt photos (Microsoft SQL Server)

CREATE TABLE dbo.InventoryItems (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryItems PRIMARY KEY,
    Name            NVARCHAR(100)  NOT NULL,
    NormalizedName  NVARCHAR(100)  NOT NULL,
    Unit            NVARCHAR(20)   NOT NULL,
    CreatedAtUtc    DATETIME2      NOT NULL,
    UpdatedAtUtc    DATETIME2      NOT NULL
);
CREATE UNIQUE INDEX IX_InventoryItems_NormalizedName ON dbo.InventoryItems (NormalizedName);

CREATE TABLE dbo.InventoryPurchases (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryPurchases PRIMARY KEY,
    PurchasedOn   DATETIME2       NOT NULL,
    Store         NVARCHAR(120)   NULL,
    Notes         NVARCHAR(1000)  NULL,
    Total         DECIMAL(12,2)   NOT NULL,
    CreatedBy     NVARCHAR(256)   NOT NULL,
    CreatedAtUtc  DATETIME2       NOT NULL,
    UpdatedAtUtc  DATETIME2       NOT NULL
);
CREATE INDEX IX_InventoryPurchases_PurchasedOn ON dbo.InventoryPurchases (PurchasedOn);

-- No cascade from items: an item that is on a purchase cannot be deleted.
CREATE TABLE dbo.InventoryPurchaseLines (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryPurchaseLines PRIMARY KEY,
    PurchaseId       INT            NOT NULL CONSTRAINT FK_InventoryPurchaseLines_InventoryPurchases REFERENCES dbo.InventoryPurchases (Id) ON DELETE CASCADE,
    InventoryItemId  INT            NOT NULL CONSTRAINT FK_InventoryPurchaseLines_InventoryItems REFERENCES dbo.InventoryItems (Id),
    Quantity         DECIMAL(12,3)  NOT NULL,
    Unit             NVARCHAR(20)   NOT NULL,
    Price            DECIMAL(10,2)  NOT NULL
);
CREATE INDEX IX_InventoryPurchaseLines_PurchaseId ON dbo.InventoryPurchaseLines (PurchaseId);
CREATE INDEX IX_InventoryPurchaseLines_InventoryItemId ON dbo.InventoryPurchaseLines (InventoryItemId);

CREATE TABLE dbo.InventoryReceipts (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryReceipts PRIMARY KEY,
    PurchaseId        INT            NOT NULL CONSTRAINT FK_InventoryReceipts_InventoryPurchases REFERENCES dbo.InventoryPurchases (Id) ON DELETE CASCADE,
    FileName          NVARCHAR(100)  NOT NULL,
    OriginalFileName  NVARCHAR(255)  NULL,
    ContentType       NVARCHAR(100)  NOT NULL,
    SizeBytes         BIGINT         NOT NULL,
    UploadedAtUtc     DATETIME2      NOT NULL
);
CREATE INDEX IX_InventoryReceipts_PurchaseId ON dbo.InventoryReceipts (PurchaseId);
