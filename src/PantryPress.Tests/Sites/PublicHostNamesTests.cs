using PantryPress.Domain.Modules.Sites.Business;

namespace PantryPress.Tests.Sites;

// PP2.12 (ARCH-003, PSEC-004): a public request's Host is read as the host to look up - port split off, IP literals
// refused, the rest in stored form - and anything refused here is never looked up.
public sealed class PublicHostNamesTests
{
    [Theory]
    [InlineData("cook.localhost", "cook.localhost")]
    [InlineData("Cook.LocalHost", "cook.localhost")]
    [InlineData("COOK.PANTRYPRESS.APP", "cook.pantrypress.app")]
    [InlineData("cook.localhost.", "cook.localhost")]
    [InlineData("cook.localhost:7053", "cook.localhost")]
    [InlineData("cook.localhost.:443", "cook.localhost")]
    [InlineData("cook.localhost:1", "cook.localhost")]
    [InlineData("cook.localhost:65535", "cook.localhost")]
    [InlineData("www.example.com:8080", "www.example.com")]
    [InlineData("xn--ck-xka.example", "xn--ck-xka.example")]
    [InlineData("1cook.localhost", "1cook.localhost")]
    public void A_host_name_is_read_in_stored_form(string header, string expected)
    {
        Assert.True(PublicHostNames.TryRead(header, out var host));
        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("localhost:5095")]
    [InlineData("cook")]
    [InlineData(".")]
    [InlineData(":443")]
    public void A_host_that_names_no_site_is_refused(string? header) =>
        Assert.False(PublicHostNames.TryRead(header, out _));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:80")]
    [InlineData("127.0.0.1.")]
    [InlineData("10.0.0.4")]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("0x7f000001")]
    [InlineData("0177.0.0.1")]
    [InlineData("127.0.0.0x1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("[::1]")]
    [InlineData("[::1]:443")]
    [InlineData("[::ffff:127.0.0.1]")]
    [InlineData("[fe80::1%25eth0]")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void An_ip_literal_in_any_spelling_is_refused(string header) =>
        Assert.False(PublicHostNames.TryRead(header, out _));

    [Theory]
    [InlineData("cook.localhost:")]
    [InlineData("cook.localhost:0")]
    [InlineData("cook.localhost:080")]
    [InlineData("cook.localhost:65536")]
    [InlineData("cook.localhost:99999")]
    [InlineData("cook.localhost:123456")]
    [InlineData("cook.localhost:+80")]
    [InlineData("cook.localhost:-1")]
    [InlineData("cook.localhost: 80")]
    [InlineData("cook.localhost:80 ")]
    [InlineData("cook.localhost:8a")]
    [InlineData("cook.localhost:٨٠")]
    [InlineData("cook.localhost:80:80")]
    public void A_bad_port_is_refused(string header) =>
        Assert.False(PublicHostNames.TryRead(header, out _));

    [Theory]
    [InlineData("cöok.localhost")]
    [InlineData("сook.localhost")]
    [InlineData("cook.localhost/evil")]
    [InlineData("cook localhost")]
    [InlineData(" cook.localhost")]
    [InlineData("cook..localhost")]
    [InlineData("-cook.localhost")]
    [InlineData("cook-.localhost")]
    [InlineData("cook.localhost..")]
    [InlineData("user@cook.localhost")]
    [InlineData("cook.localhost\t")]
    [InlineData("cook_kitchen.localhost")]
    [InlineData("cook.localhost%2e")]
    public void Anything_but_a_host_name_is_refused(string header) =>
        Assert.False(PublicHostNames.TryRead(header, out _));

    [Fact]
    public void A_host_too_long_to_be_one_is_refused()
    {
        var label = new string('a', 63);
        var name = string.Join('.', label, label, label, label[..58], "app");
        Assert.True(name.Length > 253);
        Assert.False(PublicHostNames.TryRead(name, out _));
    }
}
