namespace MindAttic.Psst.Tests.Configuration;

using Microsoft.Extensions.Configuration;
using MindAttic.Psst.Configuration;
using Xunit;

public class PsstConfigurationTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Load_EmptyConfiguration_ReturnsAllNullsAndNoErrors()
    {
        var config = PsstConfiguration.Load(Build(new Dictionary<string, string?>()));

        Assert.Null(config.Twilio);
        Assert.Null(config.Email);
        Assert.Null(config.RecipientPhoneNumber);
        Assert.Null(config.RecipientEmailSmsAddress);
        Assert.False(config.HasAnySmsTransport);
        Assert.Empty(config.Errors);
    }

    [Fact]
    public void Load_FullTwilio_PopulatesTwilioRecord()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:twilio:accountSid"] = "ACabc",
            ["MindAttic:Vault:Notifications:twilio:authToken"]  = "shh",
            ["MindAttic:Vault:Notifications:twilio:from"]       = "+15555550100",
            ["MindAttic:Vault:Notifications:to"]                = "+15555550101",
        }));

        Assert.NotNull(config.Twilio);
        Assert.Equal("ACabc", config.Twilio!.AccountSid);
        Assert.Equal("shh", config.Twilio.AuthToken);
        Assert.Equal("+15555550100", config.Twilio.From);
        Assert.Equal("+15555550101", config.RecipientPhoneNumber);
        Assert.True(config.HasAnySmsTransport);
        Assert.Empty(config.Errors);
    }

    [Fact]
    public void Load_TwilioMissingAuthToken_ReturnsNullTwilio_AndAddsError()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:twilio:accountSid"] = "ACabc",
            ["MindAttic:Vault:Notifications:twilio:from"]       = "+15555550100",
        }));

        Assert.Null(config.Twilio);
        Assert.Contains(config.Errors, e => e.Contains("twilio") && e.Contains("authToken"));
    }

    [Theory]
    [InlineData("accountSid")]
    [InlineData("authToken")]
    [InlineData("from")]
    public void Load_TwilioWhitespaceField_ReturnsNullTwilio(string fieldName)
    {
        var values = new Dictionary<string, string?>
        {
            ["MindAttic:Vault:Notifications:twilio:accountSid"] = "ACabc",
            ["MindAttic:Vault:Notifications:twilio:authToken"]  = "tok",
            ["MindAttic:Vault:Notifications:twilio:from"]       = "+15555550100",
        };
        values[$"MindAttic:Vault:Notifications:twilio:{fieldName}"] = "   ";

        var config = PsstConfiguration.Load(Build(values));

        Assert.Null(config.Twilio);
        Assert.Contains(config.Errors, e => e.Contains(fieldName));
    }

    [Fact]
    public void Load_TwilioFullButRecipientMissing_AddsError()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:twilio:accountSid"] = "ACabc",
            ["MindAttic:Vault:Notifications:twilio:authToken"]  = "shh",
            ["MindAttic:Vault:Notifications:twilio:from"]       = "+15555550100",
        }));

        Assert.NotNull(config.Twilio);
        Assert.Contains(config.Errors, e => e.Contains("'to'"));
    }

    [Fact]
    public void Load_FullEmail_PopulatesEmailRecord()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:smtpPort"] = "465",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
            ["MindAttic:Vault:Notifications:toEmail"]        = "5555550101@vtext.com",
        }));

        Assert.NotNull(config.Email);
        Assert.Equal("smtp.gmail.com", config.Email!.SmtpHost);
        Assert.Equal(465, config.Email.SmtpPort);
        Assert.Equal("5555550101@vtext.com", config.RecipientEmailSmsAddress);
        Assert.Empty(config.Errors);
    }

    [Fact]
    public void Load_EmailMissingPort_DefaultsTo587()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
        }));

        Assert.NotNull(config.Email);
        Assert.Equal(587, config.Email!.SmtpPort);
    }

    [Fact]
    public void Load_EmailUnparseablePort_DefaultsTo587()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:smtpPort"] = "not-a-number",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
        }));

        Assert.NotNull(config.Email);
        Assert.Equal(587, config.Email!.SmtpPort);
    }

    [Theory]
    [InlineData("smtpHost")]
    [InlineData("username")]
    [InlineData("password")]
    [InlineData("from")]
    public void Load_EmailMissingRequiredField_ReturnsNullEmail_AndAddsError(string fieldName)
    {
        var values = new Dictionary<string, string?>
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
        };
        values.Remove($"MindAttic:Vault:Notifications:email:{fieldName}");

        var config = PsstConfiguration.Load(Build(values));

        Assert.Null(config.Email);
        Assert.Contains(config.Errors, e => e.Contains(fieldName));
    }

    [Fact]
    public void HasAnySmsTransport_TrueWhenOnlyEmailConfigured()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
            ["MindAttic:Vault:Notifications:toEmail"]        = "5555550101@vtext.com",
        }));

        Assert.True(config.HasAnySmsTransport);
        Assert.Empty(config.Errors);
    }
}
