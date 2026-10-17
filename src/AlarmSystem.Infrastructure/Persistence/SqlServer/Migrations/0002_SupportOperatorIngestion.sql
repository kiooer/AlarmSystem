SET NOCOUNT ON;
SET XACT_ABORT ON;

ALTER TABLE alarm.AlarmEvents
    DROP CONSTRAINT CK_AlarmEvents_EventType;

ALTER TABLE alarm.AlarmEvents
    ADD CONSTRAINT CK_AlarmEvents_EventType
        CHECK (EventType IN ('ALARM', 'OPERATOR', 'ACK'));

ALTER TABLE alarm.AlarmEvents
    ADD RawPriority nvarchar(128) NULL;

ALTER TABLE alarm.AlarmEvents
    ADD RawUnit nvarchar(128) NULL;

ALTER TABLE alarm.AlarmEvents
    ADD DataQualityFlags nvarchar(256) NULL;
