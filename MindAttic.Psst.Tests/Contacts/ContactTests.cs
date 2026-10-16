namespace MindAttic.Psst.Tests.Contacts;

using MindAttic.Psst.Contacts;
using NUnit.Framework;

/// <summary>
/// Behavior tests for <see cref="ContactBook"/>. This is the lookup
/// <c>psst sms</c> uses to resolve a recipient string to a contact before
/// sending — a wrong match here texts the wrong person, so the name-before-phone
/// precedence and the phone normalization across input formats are both
/// covered explicitly.
/// </summary>
public class ContactTests
{
    private static ContactBook BookOf(params Contact[] contacts) => new(contacts);

    [Test]
    public void Find_MatchesNameCaseInsensitively()
    {
        var alice = new Contact("Alice", "5551234567");
        var book = BookOf(alice, new Contact("Bob", "5559876543"));

        Assert.That(book.Find("alice"), Is.SameAs(alice));
        Assert.That(book.Find("ALICE"), Is.SameAs(alice));
        Assert.That(book.Find("Alice"), Is.SameAs(alice));
    }

    [Test]
    public void Find_PrefersNameMatchOverPhoneMatch()
    {
        // "Bob"'s phone digit-normalizes to the same 10 digits as the name
        // "5551234567" would, if someone passed a phone number as input —
        // but here the input IS a name, so the name match must win even
        // though a different contact's phone also happens to match digits.
        var alice = new Contact("5551234567", "0000000000");
        var bob = new Contact("Bob", "5551234567");
        var book = BookOf(alice, bob);

        var found = book.Find("5551234567");

        Assert.That(found, Is.SameAs(alice));
    }

    [TestCase("+15551234567")]
    [TestCase("555-123-4567")]
    [TestCase("(555) 123-4567")]
    [TestCase("5551234567")]
    [TestCase("1-555-123-4567")]
    public void Find_FallsBackToPhoneMatchAcrossFormats(string input)
    {
        var contact = new Contact("Alice", "(555) 123-4567");
        var book = BookOf(contact);

        Assert.That(book.Find(input), Is.SameAs(contact));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Find_ReturnsNullForBlankInput(string? input)
    {
        var book = BookOf(new Contact("Alice", "5551234567"));

        Assert.That(book.Find(input!), Is.Null);
    }

    [Test]
    public void Find_ReturnsNullWhenUnmatched()
    {
        var book = BookOf(new Contact("Alice", "5551234567"));

        Assert.That(book.Find("Nobody"), Is.Null);
        Assert.That(book.Find("555-000-0000"), Is.Null);
    }

    [Test]
    public void WithAdded_RejectsBlankName()
    {
        var book = ContactBook.Empty;

        Assert.Throws<ArgumentException>(() => book.WithAdded(new Contact("  ", "5551234567")));
    }

    [Test]
    public void WithAdded_RejectsBlankPhone()
    {
        var book = ContactBook.Empty;

        Assert.Throws<ArgumentException>(() => book.WithAdded(new Contact("Alice", " ")));
    }

    [Test]
    public void WithAdded_RejectsCaseInsensitiveDuplicateName()
    {
        var book = BookOf(new Contact("Alice", "5551234567"));

        Assert.Throws<InvalidOperationException>(
            () => book.WithAdded(new Contact("ALICE", "5559876543")));
    }

    [Test]
    public void WithAdded_LeavesOriginalBookUnchanged()
    {
        var original = BookOf(new Contact("Alice", "5551234567"));

        var updated = original.WithAdded(new Contact("Bob", "5559876543"));

        Assert.That(original.Contacts, Has.Exactly(1).Items);
        Assert.That(updated.Contacts.Count, Is.EqualTo(2));
    }

    [Test]
    public void WithoutContact_RemovesOnlyFirstCaseInsensitiveMatch()
    {
        var alice1 = new Contact("Alice", "5551111111");
        var alice2 = new Contact("alice", "5552222222");
        var bob = new Contact("Bob", "5553333333");
        var book = BookOf(alice1, alice2, bob);

        var updated = book.WithoutContact("ALICE");

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Contacts, Is.EqualTo(new[] { alice2, bob }));
    }

    [Test]
    public void WithoutContact_ReturnsNullWhenAbsent()
    {
        var book = BookOf(new Contact("Alice", "5551234567"));

        Assert.That(book.WithoutContact("Nobody"), Is.Null);
    }

    [Test]
    public void WithoutContact_LeavesOriginalBookUnchanged()
    {
        var original = BookOf(new Contact("Alice", "5551234567"), new Contact("Bob", "5559876543"));

        var updated = original.WithoutContact("Alice");

        Assert.That(original.Contacts.Count, Is.EqualTo(2));
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Contacts, Has.Exactly(1).Items);
    }
}
