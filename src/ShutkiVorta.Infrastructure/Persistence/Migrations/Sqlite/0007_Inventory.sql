-- Kitchen inventory: saved item names, purchases with their items, and receipt photos (SQLite)

CREATE TABLE InventoryItems (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL,
    NormalizedName  TEXT    NOT NULL,
    Unit            TEXT    NOT NULL,
    CreatedAtUtc    TEXT    NOT NULL,
    UpdatedAtUtc    TEXT    NOT NULL
);
CREATE UNIQUE INDEX IX_InventoryItems_NormalizedName ON InventoryItems (NormalizedName);

CREATE TABLE InventoryPurchases (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PurchasedOn   TEXT    NOT NULL,
    Store         TEXT    NULL,
    Notes         TEXT    NULL,
    Total         NUMERIC NOT NULL,
    CreatedBy     TEXT    NOT NULL,
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT    NOT NULL
);
CREATE INDEX IX_InventoryPurchases_PurchasedOn ON InventoryPurchases (PurchasedOn);

-- No cascade from items: an item that is on a purchase cannot be deleted.
CREATE TABLE InventoryPurchaseLines (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PurchaseId       INTEGER NOT NULL REFERENCES InventoryPurchases (Id) ON DELETE CASCADE,
    InventoryItemId  INTEGER NOT NULL REFERENCES InventoryItems (Id),
    Quantity         NUMERIC NOT NULL,
    Unit             TEXT    NOT NULL,
    Price            NUMERIC NOT NULL
);
CREATE INDEX IX_InventoryPurchaseLines_PurchaseId ON InventoryPurchaseLines (PurchaseId);
CREATE INDEX IX_InventoryPurchaseLines_InventoryItemId ON InventoryPurchaseLines (InventoryItemId);

CREATE TABLE InventoryReceipts (
    Id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PurchaseId        INTEGER NOT NULL REFERENCES InventoryPurchases (Id) ON DELETE CASCADE,
    FileName          TEXT    NOT NULL,
    OriginalFileName  TEXT    NULL,
    ContentType       TEXT    NOT NULL,
    SizeBytes         INTEGER NOT NULL,
    UploadedAtUtc     TEXT    NOT NULL
);
CREATE INDEX IX_InventoryReceipts_PurchaseId ON InventoryReceipts (PurchaseId);
