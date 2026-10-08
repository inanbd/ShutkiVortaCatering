-- Durable email outbox + delivery log (Microsoft SQL Server)

CREATE TABLE dbo.EmailOutbox (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
    ToAddresses       NVARCHAR(2000)  NOT NULL,
    ReplyTo           NVARCHAR(256)   NULL,
    Subject           NVARCHAR(500)   NOT NULL,
    HtmlBody          NVARCHAR(MAX)   NOT NULL,
    TextBody          NVARCHAR(MAX)   NOT NULL,
    Status            INT             NOT NULL,
    Attempts          INT             NOT NULL CONSTRAINT DF_EmailOutbox_Attempts DEFAULT 0,
    LastError         NVARCHAR(4000)  NULL,
    DeliveryMethod    NVARCHAR(30)    NULL,
    DeliveryDetail    NVARCHAR(1000)  NULL,
    CreatedAtUtc      DATETIME2       NOT NULL,
    NextAttemptAtUtc  DATETIME2       NULL,
    LastAttemptAtUtc  DATETIME2       NULL,
    SentAtUtc         DATETIME2       NULL
);
CREATE INDEX IX_EmailOutbox_Status_NextAttempt ON dbo.EmailOutbox (Status, NextAttemptAtUtc);
CREATE INDEX IX_EmailOutbox_CreatedAtUtc ON dbo.EmailOutbox (CreatedAtUtc);
