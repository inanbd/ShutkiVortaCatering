-- Durable email outbox + delivery log (SQLite)

CREATE TABLE EmailOutbox (
    Id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ToAddresses       TEXT    NOT NULL,
    ReplyTo           TEXT    NULL,
    Subject           TEXT    NOT NULL,
    HtmlBody          TEXT    NOT NULL,
    TextBody          TEXT    NOT NULL,
    Status            INTEGER NOT NULL,
    Attempts          INTEGER NOT NULL DEFAULT 0,
    LastError         TEXT    NULL,
    DeliveryMethod    TEXT    NULL,
    DeliveryDetail    TEXT    NULL,
    CreatedAtUtc      TEXT    NOT NULL,
    NextAttemptAtUtc  TEXT    NULL,
    LastAttemptAtUtc  TEXT    NULL,
    SentAtUtc         TEXT    NULL
);
CREATE INDEX IX_EmailOutbox_Status_NextAttempt ON EmailOutbox (Status, NextAttemptAtUtc);
CREATE INDEX IX_EmailOutbox_CreatedAtUtc ON EmailOutbox (CreatedAtUtc);
