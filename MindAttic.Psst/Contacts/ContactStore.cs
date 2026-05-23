namespace MindAttic.Psst.Contacts;

using System.Text.Json;
using System.Text.Json.Serialization;
using MindAttic.Psst.Configuration;

/// <summary>
/// JSON persistence for the Psst contact book. The file lives next to
/// <c>settings.json</c> in the per-app roaming bucket so it's user-editable
/// without going through user secrets.
///
/// <para>Path: <c>%APPDATA%\MindAttic\Psst\contacts.json</c></para>
/// <para>Schema:</para>
/// <code>
/// {
///   "contacts": [
///     { "name": "Ryan",  "phone": "+19203764617" },
///     { "name": "Alice", "phone": "+15551234567", "defaultVia": "twilio" }
///   ]
/// }
/// </code>
/// <para>
/// <c>defaultVia</c> is optional and serialized as the lowercase
/// <see cref="PsstVia"/> name (<c>"twilio"</c> or <c>"email"</c>); omitted
/// or null means "no per-contact preference."
/// </para>
/// </summary>
public static class ContactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            // Serialize PsstVia as lowercase string ("twilio"/"email") so
            // the on-disk schema stays human-editable and matches the
            // --via CLI flag's accepted values.
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    /// <summary>Full path to <c>%APPDATA%\MindAttic\Psst\contacts.json</c>.</summary>
    public static string GetPath() =>
        Path.Combine(PsstConfigurationSources.GetAppDataDirectory(), "contacts.json");

    /// <summary>
    /// Read the contact book from disk. Returns <see cref="ContactBook.Empty"/>
    /// when the file doesn't exist or fails to parse; the CLI surfaces the
    /// file path in its output so users can hand-fix a malformed file.
    /// </summary>
    public static ContactBook Load()
    {
        var path = GetPath();
        if (!File.Exists(path)) return ContactBook.Empty;
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<ContactsFile>(json, JsonOptions);
            return new ContactBook(doc?.Contacts ?? new List<Contact>());
        }
        catch (Exception)
        {
            return ContactBook.Empty;
        }
    }

    /// <summary>
    /// Write the contact book to disk, creating the directory if needed.
    /// Overwrites the file atomically via a temp-file swap so a crash mid-
    /// write can't truncate the user's contact list.
    /// </summary>
    public static void Save(ContactBook book)
    {
        var path = GetPath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var doc = new ContactsFile { Contacts = book.Contacts.ToList() };
        var json = JsonSerializer.Serialize(doc, JsonOptions);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }

    private sealed class ContactsFile
    {
        public List<Contact> Contacts { get; set; } = new();
    }
}
