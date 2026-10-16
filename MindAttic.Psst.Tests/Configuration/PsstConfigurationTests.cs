namespace MindAttic.Psst.Tests.Configuration;

using Microsoft.Extensions.Configuration;
using MindAttic.Psst.Configuration;
using NUnit.Framework;

public class PsstConfigurationTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Test]
    public void Load_EmptyConfiguration_ReturnsAllNullsAndNoErrors()
    {
        var config = PsstConfiguration.Load(Build(new Dictionary<string, string?>()));

        Assert.That(config.Email, Is.Null);
        Assert.That(config.RecipientPhoneNumber, Is.Null);
        Assert.That(config.RecipientEmailSmsAddress, Is.Null);
        Assert.That(config.HasAnySmsTransport, Is.False);
        Assert.That(config.Errors, Is.Empty);
    }

    [Test]
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

        Assert.That(config.Email, Is.Not.Null);
        Assert.That(config.Email!.SmtpHost, Is.EqualTo("smtp.gmail.com"));
        Assert.That(config.Email.SmtpPort, Is.EqualTo(465));
        Assert.That(config.RecipientEmailSmsAddress, Is.EqualTo("5555550101@vtext.com"));
        Assert.That(config.HasAnySmsTransport, Is.True);
        Assert.That(config.Errors, Is.Empty);
    }

    [Test]
    public void Load_EmailMissingPort_DefaultsTo587()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
        }));

        Assert.That(config.Email, Is.Not.Null);
        Assert.That(config.Email!.SmtpPort, Is.EqualTo(587));
    }

    [Test]
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

        Assert.That(config.Email, Is.Not.Null);
        Assert.That(config.Email!.SmtpPort, Is.EqualTo(587));
    }

    [TestCase("0")]        // zero is not a usable TCP port
    [TestCase("-1")]       // negative
    [TestCase("70000")]    // above the 65535 ceiling
    public void Load_EmailOutOfRangePort_DefaultsTo587(string port)
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:smtpPort"] = port,
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
        }));

        Assert.That(config.Email, Is.Not.Null);
        Assert.That(config.Email!.SmtpPort, Is.EqualTo(587));
    }

    [TestCase("smtpHost")]
    [TestCase("username")]
    [TestCase("password")]
    [TestCase("from")]
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

        Assert.That(config.Email, Is.Null);
        Assert.That(config.Errors.Any(e => e.Contains(fieldName)), Is.True);
    }

    [Test]
    public void HasAnySmsTransport_TrueWhenEmailConfigured()
    {
        var config = PsstConfiguration.Load(Build(new()
        {
            ["MindAttic:Vault:Notifications:email:smtpHost"] = "smtp.gmail.com",
            ["MindAttic:Vault:Notifications:email:username"] = "u@example.com",
            ["MindAttic:Vault:Notifications:email:password"] = "pw",
            ["MindAttic:Vault:Notifications:email:from"]     = "u@example.com",
            ["MindAttic:Vault:Notifications:toEmail"]        = "5555550101@vtext.com",
        }));

        Assert.That(config.HasAnySmsTransport, Is.True);
        Assert.That(config.Errors, Is.Empty);
    }
}
