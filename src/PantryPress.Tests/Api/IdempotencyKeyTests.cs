using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PantryPress.ApiService.Hosting;

namespace PantryPress.Tests.Api;

// PP1.9: reading the key, naming the caller, fingerprinting the request, and which answers are stored.
public sealed class IdempotencyKeyTests
{
    [Theory]
    [InlineData("a", "a")]
    [InlineData("8e03978e-40d5-43e8-bc93-6894a57f9324", "8e03978e-40d5-43e8-bc93-6894a57f9324")]
    [InlineData("\"8e03978e\"", "8e03978e")] // the IETF draft's structured-field string
    [InlineData("!~#$%&'()*+-./:;<=>?@[]^_`{|}", "!~#$%&'()*+-./:;<=>?@[]^_`{|}")]
    public void A_key_of_visible_ascii_is_read(string header, string expected)
    {
        Assert.True(ApiIdempotency.TryParseKey(new StringValues(header), out var key));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void A_key_of_exactly_128_characters_is_read() =>
        Assert.True(ApiIdempotency.TryParseKey(new StringValues(new string('k', 128)), out _));

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("with space")]
    [InlineData("tab\there")]
    [InlineData("café")]
    [InlineData("\"in\"side\"")]
    [InlineData("\"back\\slash\"")]
    [InlineData("a\"b")]
    [InlineData("\"")]
    [InlineData("k1,k2")]
    public void Anything_else_is_not_a_key(string header) =>
        Assert.False(ApiIdempotency.TryParseKey(new StringValues(header), out _));

    [Fact]
    public void A_key_longer_than_128_characters_or_sent_twice_is_not_a_key()
    {
        Assert.False(ApiIdempotency.TryParseKey(new StringValues(new string('k', 129)), out _));
        Assert.False(ApiIdempotency.TryParseKey(new StringValues(["one", "two"]), out _));
    }

    [Fact]
    public void The_caller_is_the_signed_in_account_and_an_anonymous_caller_has_no_scope()
    {
        var account = Guid.NewGuid();
        var signedIn = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.ToString())], "Test"));

        Assert.True(ApiIdempotency.TryGetCallerScope(signedIn, out var scope));
        Assert.Equal($"account:{account:D}", scope);
        Assert.False(ApiIdempotency.TryGetCallerScope(new ClaimsPrincipal(new ClaimsIdentity()), out _));
    }

    [Fact]
    public async Task The_fingerprint_covers_method_path_query_and_body_and_leaves_the_body_readable()
    {
        var baseline = Request("POST", "/api/v1/sites/one/posts", "", "{\"title\":\"Pie\"}");
        var fingerprint = await Fingerprint(baseline);

        Assert.Equal(32, fingerprint.Length);
        Assert.Equal("{\"title\":\"Pie\"}", await new StreamReader(baseline.Body).ReadToEndAsync());
        Assert.Equal(fingerprint, await Fingerprint(Request("POST", "/api/v1/sites/one/posts", "", "{\"title\":\"Pie\"}")));

        // The caller and the key salt it, so a stored fingerprint is never a bare hash of a body.
        Assert.NotEqual(fingerprint, await ApiIdempotency.FingerprintAsync(
            Request("POST", "/api/v1/sites/one/posts", "", "{\"title\":\"Pie\"}"), "account:other", "key", CancellationToken.None));
        Assert.NotEqual(fingerprint, await ApiIdempotency.FingerprintAsync(
            Request("POST", "/api/v1/sites/one/posts", "", "{\"title\":\"Pie\"}"), "account:one", "other-key", CancellationToken.None));

        foreach (var different in new[]
        {
            Request("PUT", "/api/v1/sites/one/posts", "", "{\"title\":\"Pie\"}"),
            Request("POST", "/api/v1/sites/two/posts", "", "{\"title\":\"Pie\"}"),
            Request("POST", "/api/v1/sites/one/posts", "?draft=true", "{\"title\":\"Pie\"}"),
            Request("POST", "/api/v1/sites/one/posts", "", "{\"title\":\"Tart\"}"),
        })
        {
            Assert.NotEqual(fingerprint, await Fingerprint(different));
        }
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(409, true)]
    [InlineData(401, false)]
    [InlineData(408, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void Only_an_answer_a_retry_would_repeat_is_stored(int status, bool stored) =>
        Assert.Equal(stored, ApiIdempotency.IsStorable(status));

    private static Task<byte[]> Fingerprint(HttpRequest request) =>
        ApiIdempotency.FingerprintAsync(request, "account:one", "key", CancellationToken.None);

    private static HttpRequest Request(string method, string path, string query, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }
}
