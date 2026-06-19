namespace MindAttic.Psst.Configuration;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Resolves Psst's email-SMS settings out of an <see cref="IConfiguration"/>
/// composed by the host (Vault files, env vars, etc.).
///
/// <para>Schema, slotted under the Vault root:</para>
/// <code>
/// MindAttic:
///   Vault:
///     Notifications:
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
    EmailSettings? Email,
    string? RecipientPhoneNumber,
    string? RecipientEmailSmsAddress,
    IReadOnlyList<string> Errors)
{
    public const string Section = "MindAttic:Vault:Notifications";

    /// <summary>True when the email transport has enough config to attempt a send.</summary>
    public bool HasAnySmsTransport => Email is not null;

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

        var email = EmailSettings.Load(root.GetSection("email"), errors);
        var to = root["to"];
        var toEmail = root["toEmail"];

        if (email is not null && string.IsNullOrWhiteSpace(toEmail) && string.IsNullOrWhiteSpace(to))
            errors.Add("email is configured but neither 'toEmail' nor 'to' (for carrier auto-fanout) is set");

        return new PsstConfiguration(email, to, toEmail, errors);
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

        // Fall back to 587 for anything that isn't a usable TCP port —
        // unparseable, zero, negative, or above 65535 — so a typo can't push
        // a nonsense port through to ConnectAsync as a confusing runtime error.
        var port = int.TryParse(s["smtpPort"], out var p) && p is > 0 and <= 65535 ? p : 587;
        return new EmailSettings(host!, port, user!, pass!, from!);
    }
}
