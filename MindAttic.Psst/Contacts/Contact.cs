namespace MindAttic.Psst.Contacts;

using MindAttic.Psst.Sms;

/// <summary>
/// One person in the Psst contact book. <see cref="Name"/> is the lookup key
/// (case-insensitive); <see cref="Phone"/> is whatever digit-normalizable
/// shape the user typed — normalization happens at send time via
/// <see cref="CarrierGateways.NormalizeTo10Digits"/>, so the source string
/// is preserved as written. <see cref="DefaultVia"/> is the preferred SMS
/// transport for this contact (<c>null</c> = no preference, fall through to
/// env var / project default per <see cref="PsstViaResolver"/>).
/// </summary>
public sealed record Contact(string Name, string Phone, PsstVia? DefaultVia = null);

/// <summary>
/// Immutable, in-memory view of the contact list. Use <see cref="ContactStore"/>
/// to load from / save to disk. Mutating helpers (<see cref="WithAdded"/>,
/// <see cref="WithoutContact"/>) return new instances so callers can choose
/// whether to persist.
/// </summary>
public sealed class ContactBook
{
    public IReadOnlyList<Contact> Contacts { get; }

    public ContactBook(IEnumerable<Contact> contacts) =>
        Contacts = contacts.ToArray();

    public static ContactBook Empty { get; } = new(Array.Empty<Contact>());

    /// <summary>
    /// Resolve a recipient string to a contact. Tries name match first
    /// (case-insensitive), then phone-digit match. Returns null if neither
    /// — letting callers fall back to raw-phone handling.
    /// </summary>
    public Contact? Find(string nameOrPhone)
    {
        if (string.IsNullOrWhiteSpace(nameOrPhone)) return null;
        var byName = Contacts.FirstOrDefault(c =>
            c.Name.Equals(nameOrPhone, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;
        var digits = CarrierGateways.NormalizeTo10Digits(nameOrPhone);
        if (digits is null) return null;
        return Contacts.FirstOrDefault(c =>
            CarrierGateways.NormalizeTo10Digits(c.Phone) == digits);
    }

    /// <summary>
    /// Return a new book with <paramref name="contact"/> appended. Throws
    /// when name or phone is blank, or when a contact with the same name
    /// (case-insensitive) already exists.
    /// </summary>
    public ContactBook WithAdded(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.Name))
            throw new ArgumentException("name is required", nameof(contact));
        if (string.IsNullOrWhiteSpace(contact.Phone))
            throw new ArgumentException("phone is required", nameof(contact));
        if (Contacts.Any(c => c.Name.Equals(contact.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"contact '{contact.Name}' already exists");
        return new ContactBook(Contacts.Append(contact));
    }

    /// <summary>
    /// Return a new book without the contact whose name matches
    /// <paramref name="name"/> (case-insensitive). Returns null when no
    /// match is found so callers can distinguish "removed" from "no-op."
    /// </summary>
    public ContactBook? WithoutContact(string name)
    {
        var idx = -1;
        for (var i = 0; i < Contacts.Count; i++)
        {
            if (Contacts[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) return null;
        return new ContactBook(Contacts.Where((_, i) => i != idx));
    }
}
