namespace PantryPress.Tests.Gateway;

/// <summary>
/// A stand-in for the admin app's build output (PPL.2's layout, <c>dist/pantrypress-admin/browser</c>), written to a
/// folder of its own so every gateway test outside Development has pages to serve without an <c>npm run build</c>.
/// Each document says which one it is, so a test can tell which file answered.
/// </summary>
internal static class AdminAppBuild
{
    public const string BrowserMarker = "browser-rendered document";
    public const string Script = "main-YXEA34CX.js";
    public const string Chunk = "chunk-C1xRoO3R.js";
    public const string Styles = "styles-IEXM7OZ5.css";
    public const string Font = "media/fredoka-ABCD2345.woff2";
    public const string Favicon = "favicon.ico";

    /// <summary>Written beside the build, never in it: a path that left the root would find it.</summary>
    public const string Outside = "outside.txt";
    public const string OutsideSecret = "SENTINEL-outside-the-build";

    /// <summary>The public paths the gateway is configured with, as appsettings.json writes them.</summary>
    public static readonly string[] PublicPaths = ["/", "/pricing", "/terms", "/privacy", "/acceptable-use", "/dmca"];

    private static readonly Lazy<string> Shared = new(() => Create());

    /// <summary>The one unaltered build every test shares; nothing writes to it once made.</summary>
    public static string Root => Shared.Value;

    public static string Marker(string path) => $"prerendered {path}";

    /// <summary>
    /// A fresh build, changed by <paramref name="alter"/> (given its root) before anything reads it. The root is
    /// <c>browser/</c> inside a folder of its own, as Angular writes it, with a file beside it that must never be served.
    /// </summary>
    public static string Create(Action<string>? alter = null)
    {
        var parent = Directory.CreateTempSubdirectory("pantrypress-admin-build-").FullName;
        File.WriteAllText(Path.Combine(parent, Outside), OutsideSecret);
        var root = Path.Combine(parent, "browser");
        foreach (var path in PublicPaths)
        {
            Write(root, path == "/" ? "index.html" : $"{path.TrimStart('/')}/index.html", Document(Marker(path), state: true));
        }

        Write(root, "index.csr.html", Document(BrowserMarker, state: false));
        Write(root, Script, "console.log('main');");
        Write(root, Chunk, "export const chunk = 1;");
        Write(root, Styles, "body{margin:0}");
        Write(root, Font, "wOF2");
        Write(root, Favicon, "icon");
        Write(root, ".env", "SECRET=never-served");
        alter?.Invoke(root);
        return root;
    }

    public static void Write(string root, string relativePath, string content)
    {
        var file = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    // The shape Angular writes: a module script by its file, and - prerendered - the hydration state as a JSON data block,
    // which is never run and so is no inline script.
    private static string Document(string marker, bool state) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>PantryPress</title>"
        + $"<link rel=\"stylesheet\" href=\"{Styles}\"></head><body><ppa-root><main><h1>{marker}</h1></main></ppa-root>"
        + (state ? "<script id=\"ng-state\" type=\"application/json\">{\"__nghData__\":[]}</script>" : string.Empty)
        + $"<script src=\"{Script}\" type=\"module\"></script></body></html>";
}
