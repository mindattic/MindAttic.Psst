namespace MindAttic.Psst.Configuration;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Resolves Psst's Twilio + email-SMS settings out of an <see cref="IConfiguration"/>
/// composed by the host (User Secrets, env vars, etc.).
///
/// <para>Schema, slotted under the Vault root:</para>
/// <code>
/// MindAttic:
///   Vault:
///     Notifications:
///       twilio:
///         accountSid: "AC..."
///         authToken:  "..."
///         from:       "+15555550100"
///       email:
///         smtpHost:   "smtp.gmail.com"
///         smtpPort:   587
///         username:   "you@gmail.com"
///         password:   "app-password"
///         from:       "you@gmail.com"
///       to:           "+15555550101"
///       toEmail:      "5555550101@vtext.com"
/// </code>
/// </summary>
public sealed record PsstConfiguration(
    TwilioSettings? Twilio,
    EmailSettings? Email,
    string? RecipientPhoneNumber,
    string? RecipientEmailSmsAddress)
{
    public const string Section = "MindAttic:Vault:Notifications";

    /// <summary>True when at least one delivery path has enough config to attempt a send.</summary>
    public bool HasAnySmsTransport => Twilio is not null || Email is not null;

    /// <summary>
    /// Pull settings from the given configuration. Missing/empty values produce
    /// null sub-records so callers can branch on what's actually wired up.
    /// </summary>
    public static PsstConfiguration Load(IConfiguration configuration)
    {
        var root = configuration.GetSection(Section);
        return new PsstConfiguration(
            TwilioSettings.Load(root.GetSection("twilio")),
            EmailSettings.Load(root.GetSection("email")),
            root["to"],
            root["toEmail"]);
    }
}

public sealed record TwilioSettings(string AccountSid, string AuthToken, string From)
{
    public static TwilioSettings? Load(IConfigurationSection s)
    {
        var sid = s["accountSid"];
        var tok = s["authToken"];
        var from = s["from"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(tok) || string.IsNullOrWhiteSpace(from))
            return null;
        return new TwilioSettings(sid, tok, from);
    }
}

public sealed record EmailSettings(string SmtpHost, int SmtpPort, string Username, string Password, string From)
{
    public static EmailSettings? Load(IConfigurationSection s)
    {
        var host = s["smtpHost"];
        var user = s["username"];
        var pass = s["password"];
        var from = s["from"];
        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(pass) ||
            string.IsNullOrWhiteSpace(from))
            return null;
        var port = int.TryParse(s["smtpPort"], out var p) ? p : 587;
        return new EmailSettings(host, port, user, pass, from);
    }
}
