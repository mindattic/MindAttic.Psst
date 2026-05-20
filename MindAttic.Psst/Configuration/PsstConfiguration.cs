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
    string? RecipientEmailSmsAddress,
    IReadOnlyList<string> Errors)
{
    public const string Section = "MindAttic:Vault:Notifications";

    /// <summary>True when at least one delivery path has enough config to attempt a send.</summary>
    public bool HasAnySmsTransport => Twilio is not null || Email is not null;

    /// <summary>
    /// Pull settings from the given configuration. Missing/empty values produce
    /// null sub-records so callers can branch on what's actually wired up.
    /// Partial-config diagnostics are exposed via <see cref="Errors"/> so the
    /// CLI can tell the user *which* field is missing.
    /// </summary>
    public static PsstConfiguration Load(IConfiguration configuration)
    {
        var root = configuration.GetSection(Section);
        var errors = new List<string>();

        var twilio = TwilioSettings.Load(root.GetSection("twilio"), errors);
        var email = EmailSettings.Load(root.GetSection("email"), errors);
        var to = root["to"];
        var toEmail = root["toEmail"];

        if (twilio is not null && string.IsNullOrWhiteSpace(to))
            errors.Add("twilio is configured but 'to' (recipient phone) is missing");
        if (email is not null && string.IsNullOrWhiteSpace(toEmail))
            errors.Add("email is configured but 'toEmail' (recipient gateway address) is missing");

        return new PsstConfiguration(twilio, email, to, toEmail, errors);
    }
}

public sealed record TwilioSettings(string AccountSid, string AuthToken, string From)
{
    public static TwilioSettings? Load(IConfigurationSection s, List<string> errors)
    {
        var sid = s["accountSid"];
        var tok = s["authToken"];
        var from = s["from"];

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(sid)) missing.Add("accountSid");
        if (string.IsNullOrWhiteSpace(tok)) missing.Add("authToken");
        if (string.IsNullOrWhiteSpace(from)) missing.Add("from");

        if (missing.Count == 3) return null;
        if (missing.Count > 0)
        {
            errors.Add($"twilio is partially configured — missing: {string.Join(", ", missing)}");
            return null;
        }
        return new TwilioSettings(sid!, tok!, from!);
    }
}

public sealed record EmailSettings(string SmtpHost, int SmtpPort, string Username, string Password, string From)
{
    public static EmailSettings? Load(IConfigurationSection s, List<string> errors)
    {
        var host = s["smtpHost"];
        var user = s["username"];
        var pass = s["password"];
        var from = s["from"];

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(host)) missing.Add("smtpHost");
        if (string.IsNullOrWhiteSpace(user)) missing.Add("username");
        if (string.IsNullOrWhiteSpace(pass)) missing.Add("password");
        if (string.IsNullOrWhiteSpace(from)) missing.Add("from");

        if (missing.Count == 4) return null;
        if (missing.Count > 0)
        {
            errors.Add($"email is partially configured — missing: {string.Join(", ", missing)}");
            return null;
        }

        var port = int.TryParse(s["smtpPort"], out var p) ? p : 587;
        return new EmailSettings(host!, port, user!, pass!, from!);
    }
}
