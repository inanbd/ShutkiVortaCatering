-- Kitchen-imposed pauses on standing orders; emails whose content is removed after delivery (Microsoft SQL Server)

ALTER TABLE dbo.StandingOrders ADD PausedByKitchen BIT NOT NULL CONSTRAINT DF_StandingOrders_PausedByKitchen DEFAULT 0;
ALTER TABLE dbo.EmailOutbox ADD IsSensitive BIT NOT NULL CONSTRAINT DF_EmailOutbox_IsSensitive DEFAULT 0;
