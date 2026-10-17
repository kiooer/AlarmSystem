using AlarmSystem.Domain.Events;

namespace AlarmSystem.Domain.Tests.Events;

public sealed class AlarmEventTypeTests
{
    [Fact]
    public void Operator_IsAnExplicitRawEventTypeWithoutChangingExistingValues()
    {
        Assert.Equal(1, (int)AlarmEventType.Alarm);
        Assert.Equal(2, (int)AlarmEventType.Ack);
        Assert.Equal(3, (int)AlarmEventType.Operator);
    }
}
