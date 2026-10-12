using System.Net;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.IntegrationTests.Notifications.Email;

public sealed class EmailTemplateProviderTests
{
    [Theory]
    [InlineData(AlarmSeverity.High, "HIGH", "#C83C3C")]
    [InlineData(AlarmSeverity.Medium, "MEDIUM", "#D97706")]
    [InlineData(AlarmSeverity.Low, "LOW", "#2563EB")]
    public void TryRender_MapsSeverityAndBuildsBothBodies(
        AlarmSeverity severity,
        string severityLabel,
        string color)
    {
        var provider = CreateProvider();
        var notification = CreateNotification(severity);

        var rendered = provider.TryRender(notification, out var content);

        Assert.True(rendered);
        Assert.NotNull(content);
        Assert.StartsWith($"[{severityLabel}]", content.Subject, StringComparison.Ordinal);
        Assert.Contains(color, content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Test equipment", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(notification.EventId.ToString("D"), content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(notification.EventId.ToString("D"), content.PlainTextBody, StringComparison.Ordinal);
        Assert.Contains("2026-10-12 08:30:00 +08:00", content.PlainTextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRender_HtmlEncodesAllAlarmControlledText()
    {
        var provider = CreateProvider();
        var notification = new AlarmNotification(
            Guid.Parse("684ecb47-1825-4773-9508-f3ecec685974"),
            "TAG<&\"",
            "Area<script>",
            AlarmEventType.Alarm,
            AlarmSeverity.High,
            "Line 1 & <b>unsafe</b>\nLine 2",
            new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.FromHours(8)),
            "Equipment > one");

        Assert.True(provider.TryRender(notification, out var content));

        Assert.DoesNotContain("<script>", content!.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>unsafe</b>", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode(notification.Tag), content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Line 1 &amp; &lt;b&gt;unsafe&lt;/b&gt;<br />Line 2", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(notification.Message, content.PlainTextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessage_SupportsMultipleRecipientsAndAlternativeBodies()
    {
        var provider = CreateProvider();
        var builder = new EmailMessageBuilder();
        var options = CreateOptions();
        Assert.True(provider.TryRender(CreateNotification(AlarmSeverity.High), out var content));

        var message = builder.BuildMessage(content!, options);

        Assert.Equal(2, message.To.Count);
        Assert.Equal("AlarmSystem", message.From.Mailboxes.Single().Name);
        var multipart = Assert.IsType<MimeKit.MultipartAlternative>(message.Body);
        Assert.Equal(2, multipart.Count);
        Assert.Contains(multipart, entity => entity.ContentType.MimeType == "text/plain");
        Assert.Contains(multipart, entity => entity.ContentType.MimeType == "text/html");
    }

    [Fact]
    public void Constructor_MarksMissingTemplateUnhealthy()
    {
        var provider = new EmailTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "missing-email-template.html"),
            new EmailMessageBuilder());

        Assert.False(provider.IsHealthy);
        Assert.Equal("email_template_unhealthy", provider.FailureCode);
        Assert.False(provider.TryRender(CreateNotification(AlarmSeverity.High), out _));
    }

    [Fact]
    public void Constructor_MarksTemplateWithMissingPlaceholderUnhealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"email-template-{Guid.NewGuid():N}.html");
        try
        {
            File.WriteAllText(path, "<html>{{severityLabel}}</html>");

            var provider = new EmailTemplateProvider(path, new EmailMessageBuilder());

            Assert.False(provider.IsHealthy);
            Assert.Equal("email_template_unhealthy", provider.FailureCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static EmailTemplateProvider CreateProvider() => new(
        Path.Combine(AppContext.BaseDirectory, "templates", "email-alarm.html"),
        new EmailMessageBuilder());

    private static EmailOptions CreateOptions() => new()
    {
        Enabled = true,
        Host = "smtp.example.invalid",
        Port = 587,
        SecurityMode = EmailSecurityMode.StartTls,
        AuthenticationMode = EmailAuthenticationMode.Password,
        Username = "smtp-user",
        Password = "unit-test-password",
        SenderAddress = "alarm@example.invalid",
        SenderDisplayName = "AlarmSystem",
        Recipients = ["one@example.invalid", "two@example.invalid"],
        DestinationReference = "Operations",
    };

    private static AlarmNotification CreateNotification(AlarmSeverity severity) => new(
        Guid.Parse("4d84ed1f-c138-4dd5-b99b-c218af6575f7"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Alarm,
        severity,
        "Test message",
        new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.FromHours(8)),
        "Test equipment");
}
