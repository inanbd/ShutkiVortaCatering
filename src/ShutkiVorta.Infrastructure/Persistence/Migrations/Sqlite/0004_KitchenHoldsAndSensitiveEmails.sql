-- Kitchen-imposed pauses on standing orders; emails whose content is removed after delivery (SQLite)

ALTER TABLE StandingOrders ADD COLUMN PausedByKitchen INTEGER NOT NULL DEFAULT 0;
ALTER TABLE EmailOutbox ADD COLUMN IsSensitive INTEGER NOT NULL DEFAULT 0;
