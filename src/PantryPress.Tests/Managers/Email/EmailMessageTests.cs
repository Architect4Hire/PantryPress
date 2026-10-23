using PantryPress.Domain.Managers.Email;

namespace PantryPress.Tests.Managers.Email;

// PP1.2a: a message is one bare recipient and a one-line subject, so no caller can inject headers or recipients, and
// it never prints personal data.
public sealed class EmailMessageTests
{
    private static readonly Guid Id = Guid.Parse("0199f0aa-1111-7000-8000-000000000001");

    [Fact]
    public void Valid_message_keeps_its_parts()
    {
        var message = new EmailMessage(Id, "cook@example.test", "Confirm your email", "Text", "<p>Html</p>");

        Assert.Equal(Id, message.Id);
        Assert.Equal("cook@example.test", message.To);
        Assert.Equal("Confirm your email", message.Subject);
        Assert.Equal("Text", message.TextBody);
        Assert.Equal("<p>Html</p>", message.HtmlBody);
    }

    [Fact]
    public void Message_needs_an_id()
    {
        Assert.Throws<ArgumentException>(() => new EmailMessage(Guid.Empty, "cook@example.test", "Subject", "Text"));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("Cook <cook@example.test>")]
    [InlineData("cook@example.test, other@example.test")]
    [InlineData("cook@example.test\r\nBcc: other@example.test")]
    [InlineData("")]
    public void Recipient_must_be_one_bare_address(string to)
    {
        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, to, "Subject", "Text"));
    }

    [Fact]
    public void Recipient_cannot_exceed_the_smtp_path_limit()
    {
        var to = new string('a', 64) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".test";

        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, to, "Subject", "Text"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Hello\r\nBcc: other@example.test")]
    [InlineData("Hello\nWorld")]
    [InlineData("Hello\tWorld")]
    public void Subject_must_be_one_non_empty_line(string subject)
    {
        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, "cook@example.test", subject, "Text"));
    }

    // Unicode next line, line separator, and paragraph separator: some clients treat them as line breaks.
    [Theory]
    [InlineData(0x0085)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    public void Subject_rejects_unicode_line_breaks(int codePoint)
    {
        var subject = "Hello" + (char)codePoint + "World";

        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, "cook@example.test", subject, "Text"));
    }

    [Fact]
    public void Subject_has_a_length_limit()
    {
        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, "cook@example.test", new string('x', 201), "Text"));
    }

    [Fact]
    public void Bodies_cannot_be_blank()
    {
        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, "cook@example.test", "Subject", " "));
        Assert.Throws<ArgumentException>(() => new EmailMessage(Id, "cook@example.test", "Subject", "Text", " "));
    }

    // A logged message or exception must not leak the reader's address or a sign-in link (backend.md).
    [Fact]
    public void Text_form_shows_only_the_id()
    {
        var message = new EmailMessage(Id, "cook@example.test", "Confirm your email", "https://link/secret", "<a>x</a>");

        Assert.Equal($"EmailMessage {{ Id = {Id} }}", message.ToString());
    }
}
