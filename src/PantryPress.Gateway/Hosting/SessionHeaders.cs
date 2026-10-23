namespace PantryPress.Gateway.Hosting;

/// <summary>
/// The contract between this gateway and ApiService for a signed-in caller (ACCT-002; PP1.6). Mirrors ApiService's
/// <c>GatewaySession</c> - the gateway holds no reference to the API's assembly, exactly as <see cref="GatewayApi"/>
/// mirrors its problem vocabulary - and a test pins the two together.
/// </summary>
internal static class SessionHeaders
{
    /// <summary>Proves to the API that a request came from here. Without it the API reads no identity at all.</summary>
    public const string Secret = "X-PP-Gateway";

    public const string Account = "X-PP-Account";

    /// <summary>The security stamp the session was issued against; the API ends a session whose stamp moved on.</summary>
    public const string Stamp = "X-PP-Stamp";

    public const string SecondFactor = "X-PP-Mfa";

    public const string True = "true";

    /// <summary>On a response from the API: this session is over, so the cookie goes.</summary>
    public const string Ended = "X-PP-Session";

    public const string EndedValue = "ended";

    /// <summary>On a response from the API: the request rotated the stamp, so the cookie is re-issued against it.</summary>
    public const string NewStamp = "X-PP-New-Stamp";

    /// <summary>
    /// Everything a client might send that would otherwise be mistaken for the gateway's own voice. Stripped from
    /// every request the gateway forwards to the API, signed in or not.
    /// </summary>
    public static readonly string[] Request = [Secret, Account, Stamp, SecondFactor];

    /// <summary>Read and removed on the way back, so a browser never sees either.</summary>
    public static readonly string[] Response = [Ended, NewStamp];
}

/// <summary>The YARP routes declared in configuration, by id (<c>appsettings.json</c>).</summary>
internal static class GatewayRoutes
{
    /// <summary>The admin API surface forwarded to ApiService (PP1.5).</summary>
    public const string AdminApi = "api-v1";
}
