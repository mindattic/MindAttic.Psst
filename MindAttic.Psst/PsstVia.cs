namespace MindAttic.Psst;

/// <summary>
/// Which SMS transport a given send should use. Picked per-send rather than
/// per-process so a long-running shell can mix transports without restarting.
/// </summary>
public enum PsstVia
{
    /// <summary>
    /// Carrier email-to-SMS gateway (e.g. <c>5551234567@vtext.com</c>). Free,
    /// no carrier registration required.
    /// </summary>
    Email,
}

/// <summary>
/// Resolves the effective <see cref="PsstVia"/> for one send by walking the
/// precedence chain: <c>PSST_VIA</c> env var &gt; per-contact default &gt;
/// project default (<see cref="PsstVia.Email"/>).
/// </summary>
public static class PsstViaResolver
{
    public const string EnvVarName = "PSST_VIA";

    /// <summary>
    /// Apply precedence and return the chosen transport. Each input is
    /// nullable so callers can pass <c>null</c> when the corresponding source
    /// is absent; any string that doesn't parse to a known via is treated as
    /// "not provided" and the chain falls through. Returns the project
    /// default (<see cref="PsstVia.Email"/>) when nothing parses.
    /// </summary>
    public static PsstVia Resolve(string? contactDefault, string? envVarValue = null)
    {
        var envValue = envVarValue ?? Environment.GetEnvironmentVariable(EnvVarName);
        if (TryParse(envValue, out var fromEnv)) return fromEnv;
        if (TryParse(contactDefault, out var fromContact)) return fromContact;
        return PsstVia.Email;
    }

    /// <summary>
    /// Parse the user-facing string form (<c>email</c>, case-insensitive).
    /// Whitespace-only and unrecognized inputs return <c>false</c>.
    /// </summary>
    public static bool TryParse(string? value, out PsstVia via)
    {
        via = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Trim().Equals("email", StringComparison.OrdinalIgnoreCase)) { via = PsstVia.Email; return true; }
        return false;
    }

    /// <summary>
    /// User-facing string form of <paramref name="via"/> — lowercased to
    /// match the CLI convention.
    /// </summary>
    public static string Format(PsstVia via) => via switch
    {
        PsstVia.Email => "email",
        _             => via.ToString().ToLowerInvariant(),
    };
}
