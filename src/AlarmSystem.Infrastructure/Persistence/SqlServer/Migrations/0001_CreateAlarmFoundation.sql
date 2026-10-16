SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'alarm') IS NULL
    EXEC(N'CREATE SCHEMA [alarm] AUTHORIZATION [dbo];');

IF OBJECT_ID(N'alarm.SchemaMigrations', N'U') IS NULL
BEGIN
    CREATE TABLE alarm.SchemaMigrations
    (
        MigrationId nvarchar(128) NOT NULL,
        ChecksumSha256 char(64) NOT NULL,
        AppliedAtUtc datetime2(7) NOT NULL,
        ToolVersion nvarchar(64) NOT NULL,
        CONSTRAINT PK_SchemaMigrations PRIMARY KEY (MigrationId)
    );
END;

IF OBJECT_ID(N'alarm.AlarmEvents', N'U') IS NULL
BEGIN
    CREATE TABLE alarm.AlarmEvents
    (
        EventId uniqueidentifier NOT NULL,
        SourceSystem nvarchar(32) NOT NULL,
        SourceMessageId uniqueidentifier NULL,
        SourceFingerprint binary(32) NULL,
        OriginalMessageType nvarchar(32) NOT NULL,
        EventType varchar(16) NOT NULL,
        SourceNode nvarchar(128) NULL,
        OperatorNode nvarchar(128) NULL,
        TagName nvarchar(256) NULL,
        AlarmStatus nvarchar(32) NULL,
        AlarmLevel varchar(10) NULL,
        Area nvarchar(256) NULL,
        RawValue nvarchar(256) NULL,
        Description nvarchar(2048) NULL,
        SourceEventTime datetime2(7) NOT NULL,
        SourceTimeZoneId nvarchar(64) NULL,
        EventTimeUtc datetime2(7) NULL,
        IngestedAtUtc datetime2(7) NOT NULL,
        RelatedAlarmId uniqueidentifier NULL,
        AckMatchStatus varchar(20) NOT NULL,
        CONSTRAINT PK_AlarmEvents PRIMARY KEY (EventId),
        CONSTRAINT CK_AlarmEvents_EventType
            CHECK (EventType IN ('ALARM', 'ACK')),
        CONSTRAINT CK_AlarmEvents_AlarmLevel
            CHECK (AlarmLevel IS NULL OR AlarmLevel IN ('HIGH', 'MEDIUM', 'LOW')),
        CONSTRAINT CK_AlarmEvents_AckMatchStatus
            CHECK (AckMatchStatus IN ('NotApplicable', 'Matched', 'Unmatched', 'Ambiguous'))
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmEvents')
      AND name = N'UX_AlarmEvents_SourceMessageId'
)
    CREATE UNIQUE INDEX UX_AlarmEvents_SourceMessageId
        ON alarm.AlarmEvents (SourceSystem, SourceMessageId)
        WHERE SourceMessageId IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmEvents')
      AND name = N'UX_AlarmEvents_SourceFingerprint'
)
    CREATE UNIQUE INDEX UX_AlarmEvents_SourceFingerprint
        ON alarm.AlarmEvents (SourceSystem, SourceFingerprint)
        WHERE SourceFingerprint IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmEvents')
      AND name = N'IX_AlarmEvents_SourceEventTime_EventId'
)
    CREATE INDEX IX_AlarmEvents_SourceEventTime_EventId
        ON alarm.AlarmEvents (SourceEventTime, EventId);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmEvents')
      AND name = N'IX_AlarmEvents_EventTimeUtc'
)
    CREATE INDEX IX_AlarmEvents_EventTimeUtc
        ON alarm.AlarmEvents (EventTimeUtc)
        WHERE EventTimeUtc IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmEvents')
      AND name = N'IX_AlarmEvents_RelatedAlarmId'
)
    CREATE INDEX IX_AlarmEvents_RelatedAlarmId
        ON alarm.AlarmEvents (RelatedAlarmId)
        WHERE RelatedAlarmId IS NOT NULL;

IF OBJECT_ID(N'alarm.AlarmStates', N'U') IS NULL
BEGIN
    CREATE TABLE alarm.AlarmStates
    (
        AlarmId uniqueidentifier NOT NULL,
        SourceSystem nvarchar(32) NOT NULL,
        SourceNode nvarchar(128) NULL,
        TagName nvarchar(256) NULL,
        AlarmStatus nvarchar(32) NOT NULL,
        AlarmLevel varchar(10) NULL,
        Area nvarchar(256) NULL,
        CurrentValue nvarchar(256) NULL,
        NormalizedValue nvarchar(256) NULL,
        AlarmVersion int NOT NULL,
        FirstEventId uniqueidentifier NOT NULL,
        CurrentEventId uniqueidentifier NOT NULL,
        FirstAlarmSourceTime datetime2(7) NOT NULL,
        VersionSourceTime datetime2(7) NOT NULL,
        LastAlarmTimeUtc datetime2(7) NULL,
        VersionStartedAtUtc datetime2(7) NULL,
        IsAcknowledged bit NOT NULL,
        AcknowledgedAtUtc datetime2(7) NULL,
        AckSourceMessageId uniqueidentifier NULL,
        ReminderCount int NOT NULL,
        MaxReminders int NULL,
        LastNotificationAtUtc datetime2(7) NULL,
        NextReminderAtUtc datetime2(7) NULL,
        IsCurrent bit NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_AlarmStates PRIMARY KEY (AlarmId),
        CONSTRAINT FK_AlarmStates_FirstEvent
            FOREIGN KEY (FirstEventId) REFERENCES alarm.AlarmEvents (EventId),
        CONSTRAINT FK_AlarmStates_CurrentEvent
            FOREIGN KEY (CurrentEventId) REFERENCES alarm.AlarmEvents (EventId),
        CONSTRAINT CK_AlarmStates_AlarmLevel
            CHECK (AlarmLevel IS NULL OR AlarmLevel IN ('HIGH', 'MEDIUM', 'LOW')),
        CONSTRAINT CK_AlarmStates_AlarmVersion CHECK (AlarmVersion >= 1),
        CONSTRAINT CK_AlarmStates_ReminderCount CHECK (ReminderCount >= 0),
        CONSTRAINT CK_AlarmStates_MaxReminders
            CHECK (MaxReminders IS NULL OR MaxReminders >= 0)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmStates')
      AND name = N'IX_AlarmStates_SourceTagStatus'
)
    CREATE INDEX IX_AlarmStates_SourceTagStatus
        ON alarm.AlarmStates (SourceSystem, SourceNode, TagName, AlarmStatus);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'alarm.AlarmStates')
      AND name = N'IX_AlarmStates_CurrentReminder'
)
    CREATE INDEX IX_AlarmStates_CurrentReminder
        ON alarm.AlarmStates (IsCurrent, IsAcknowledged, NextReminderAtUtc);

IF OBJECT_ID(N'alarm.PollingCheckpoints', N'U') IS NULL
BEGIN
    CREATE TABLE alarm.PollingCheckpoints
    (
        SourceSystem nvarchar(32) NOT NULL,
        StreamName varchar(32) NOT NULL,
        WatermarkSourceTime datetime2(7) NULL,
        LastProcessedMessageId uniqueidentifier NULL,
        LastCompletedAtUtc datetime2(7) NULL,
        LastErrorCode nvarchar(64) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_PollingCheckpoints PRIMARY KEY (SourceSystem, StreamName),
        CONSTRAINT CK_PollingCheckpoints_StreamName
            CHECK (StreamName IN ('ALARM', 'ACK_OPERATOR'))
    );
END;
