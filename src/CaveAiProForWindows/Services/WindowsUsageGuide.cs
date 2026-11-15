namespace CaveAiProForWindows.Services;

/// <summary>
/// How-to answers for the Windows Cave AI box. Measurement questions stay with
/// <see cref="CaveAiOfflineBrain"/> survey math.
/// </summary>
public static class WindowsUsageGuide
{
    public static string? TryAnswer(string? query)
    {
        var q = Normalize(query);
        if (!IsUsageQuestion(q))
            return null;
        return Render(BestTopic(q), q);
    }

    private static string Normalize(string? query)
    {
        var text = (query ?? "").Trim().ToLowerInvariant();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text;
    }

    private static bool IsUsageQuestion(string q)
    {
        if (q.Length < 8)
            return false;
        if (q.Contains("how deep", StringComparison.Ordinal) ||
            q.Contains("how far", StringComparison.Ordinal) ||
            q.Contains("how long", StringComparison.Ordinal) ||
            q.Contains("how many", StringComparison.Ordinal) ||
            q.Contains("max depth", StringComparison.Ordinal) ||
            q.Contains("the depth", StringComparison.Ordinal) ||
            q.Contains("my depth", StringComparison.Ordinal) ||
            q.Contains("shot count", StringComparison.Ordinal) ||
            q.Contains("misclosure", StringComparison.Ordinal) ||
            q.Contains("what should i", StringComparison.Ordinal) ||
            q.Contains("last leg", StringComparison.Ordinal))
            return false;
        return q.Contains("how do i", StringComparison.Ordinal) ||
               q.Contains("how to", StringComparison.Ordinal) ||
               q.Contains("how can i", StringComparison.Ordinal) ||
               q.Contains("how does", StringComparison.Ordinal) ||
               q.Contains("where is", StringComparison.Ordinal) ||
               q.Contains("where do i", StringComparison.Ordinal) ||
               q.Contains("walk me through", StringComparison.Ordinal) ||
               q.Contains("show me how", StringComparison.Ordinal) ||
               q.Contains("user guide", StringComparison.Ordinal) ||
               q.Contains("instructions", StringComparison.Ordinal) ||
               q.Contains("how to use", StringComparison.Ordinal) ||
               q.Contains("teach me", StringComparison.Ordinal);
    }

    private static string BestTopic(string q)
    {
        var best = "overview";
        var bestScore = 0;
        foreach (var topic in Topics)
        {
            var score = 0;
            foreach (var keyword in topic.Keywords)
            {
                if (q.Contains(keyword, StringComparison.Ordinal))
                    score += keyword.Length >= 8 ? 3 : 2;
            }

            if (score > bestScore)
            {
                best = topic.Id;
                bestScore = score;
            }
        }

        return best;
    }

    private static string Render(string id, string query)
    {
        foreach (var topic in Topics)
        {
            if (topic.Id != id)
                continue;
            var pieces = SelectPieces(query, SplitPieces(topic.Body));
            var body = pieces.Count == 0 ? topic.Body : string.Join("\n\n", pieces);
            return "Here is how that works in CaveAI Pro.\n\n" +
                   topic.Title + "\n" + body +
                   "\n\nAsk a more specific how-to to get the next step. On the phone, the same kind of question is answered from the User guide.";
        }

        return Render("overview", query);
    }

    private static readonly string[] StopWords =
    [
        "how", "do", "did", "does", "you", "the", "and", "for", "with", "from", "your", "about",
        "this", "that", "app", "program", "caveai", "pro", "use", "using", "work", "works",
        "where", "what", "when", "are", "can", "please", "show", "tell", "walk", "through", "teach",
    ];

    private static List<string> QueryTokens(string query)
    {
        var seen = new List<string>();
        foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n', ',', '.', '?', '!', ':', ';', '/', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim().ToLowerInvariant();
            if (word.Length < 3 || Array.IndexOf(StopWords, word) >= 0)
                continue;
            if (!seen.Contains(word))
                seen.Add(word);
        }
        return seen;
    }

    private static List<string> SplitPieces(string body)
    {
        var pieces = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length >= 12)
                pieces.Add(trimmed);
        }
        return pieces;
    }

    private static List<string> SelectPieces(string query, List<string> pieces)
    {
        if (pieces.Count == 0)
            return pieces;
        var tokens = QueryTokens(query);
        if (tokens.Count == 0)
            return pieces.Take(5).ToList();
        var hits = pieces.Where(piece => tokens.Any(token => piece.Contains(token, StringComparison.OrdinalIgnoreCase))).ToList();
        if (hits.Count == 0)
            return pieces.Take(2).ToList();
        if (hits.Count <= 5)
            return hits;
        var ranked = hits
            .Select(piece => (
                piece,
                score: tokens.Sum(token => piece.Contains(token, StringComparison.OrdinalIgnoreCase) ? (token.Length >= 6 ? 2 : 1) : 0)))
            .OrderByDescending(row => row.score)
            .Take(5)
            .Select(row => row.piece)
            .ToHashSet(StringComparer.Ordinal);
        return pieces.Where(ranked.Contains).ToList();
    }
    private readonly record struct Topic(string Id, string Title, string Body, string[] Keywords);

    private static readonly Topic[] Topics =
    [
        new(
            "overview",
            "How the Windows app is organized",
            "File -> Open a CaveAI Pro backup (.json or .zip) from the phone (Downloads/CaveAI). " +
            "PLAN and SECTION tabs are check sheets: pan and zoom. Draw in Sketch Editor. " +
            "Survey QC, Loop closure, and Network anchors are under Tools. " +
            "Help -> Keyboard shortcuts lists the map keys. The first-run window is the short getting-started path.",
            ["use this", "getting started", "how it works", "organized", "instructions", "user guide", "windows app"]),
        new(
            "open",
            "Open a survey",
            "1. On the phone, Map -> Export -> DESKTOP SYNC -> EXPORT TO WINDOWS PC, or Home -> Full library backup. The ZIP lands in Downloads/CaveAI.\n" +
            "2. Copy it to the PC by USB or a cloud folder.\n" +
            "3. File -> Open, or drag the ZIP onto the window.\n" +
            "4. Select the project. Save project writes your office edits back into that file. " +
            "When you are already signed in, that save also uploads the private survey to Survey Cloud so the phone can open it. " +
            "A newer copy already in Survey Cloud is not replaced. Push to Cloud is still the step that publishes to the public catalog.",
            ["open", "zip", "backup", "import file", "load survey"]),
        new(
            "section",
            "Draw the plan and the section",
            "1. Open the survey.\n" +
            "2. Tools -> Design plan or section from survey, or open Sketch Editor.\n" +
            "3. Choose Plan or Section. PLAN and SECTION tabs stay check sheets. You draw only in Sketch Editor.\n" +
            "4. Generate from survey. On Plan it uses left/right LRUD. On Section it draws ceiling and floor from Up/Down along the traverse.\n" +
            "5. Edit the lines and symbols, then Save project. The section stays in the same file.\n" +
            "6. On the phone, that file opens on Profile (long profile). The message is “Long profile is open. The section drawing is on this sheet.” " +
            "The phone Section tab stays radial (distance from the entrance) for older sketches. " +
            "The website shows the same drawing on the long profile.",
            ["section", "sketch", "generate", "plan", "ceiling", "floor", "profile", "draw", "cartograph"]),
        new(
            "loop",
            "Loop closure and network anchors",
            "1. Tools -> Loop closure assistant. Compass rule and weighted least squares adjust one loop. Network least squares moves every free station at once.\n" +
            "2. Raw shots stay unchanged. The result is plan station overrides.\n" +
            "3. Tools -> Network anchors before a network solve: equates merge two station names, Hold keeps a station fixed (optional east/north/up metres). Legacy Walls #equate and #fix land in the same fields.\n" +
            "4. A misclosure of 1 m or more is a publish warning, not a block. Re-measure the worst loop before you publish.",
            ["loop", "closure", "least squares", "compass", "wls", "equate", "anchor", "fixed station"]),
        new(
            "import",
            "Survex, Therion, and legacy centerline",
            "File -> Import Survex (.svx), Therion (.th), or a legacy centerline (Compass .dat, Walls .srv, Visual Topo .tro). " +
            "Export Survex, Therion, DXF, SVG, and PDF from the export menus after the survey is open. " +
            "Publication sheet (Tools) builds a composite sheet. It does not replace the map tabs.",
            ["survex", "therion", "dxf", "compass dat", "walls", "visual topo", "export", "publication"]),
        new(
            "cloud",
            "Public Library and the phone",
            "Sign in with the same Google account as Android and the website. " +
            "Help -> Public Cave Library browses the published catalog. " +
            "Push to Cloud publishes a cave to that catalog. " +
            "Saving the project while you are already signed in uploads the private survey to Survey Cloud so the phone can open it. A newer cloud copy is not replaced. " +
            "Tools -> Survey instrument records a leg from a paired DistoX, CaveBLE, or BRIC. " +
            "Tools -> Cloud plan asks Cloud AI for a plan image from the centerline and stores it on the project. " +
            "Tools -> Return to entrance shows a live Windows GPS bearing to the entrance and can sound a local alarm. The phone SOS is still the one to carry underground. " +
            "Surface map -> Import DEM stores a north-up raster with its corners for the phone. " +
            "Explore with no network opens the cached surface pack on the Surface map tab. " +
            "Bluetooth to the phone (Tools -> Android Bluetooth companion) exchanges lean survey JSON only. Photos and full ZIP backups still use Desktop Sync.",
            ["publish", "library", "cloud", "sign in", "bluetooth", "phone", "website"]),
        new(
            "lidar",
            "LiDAR scanner",
            "1. On the phone, open the survey, then Map, then LiDAR Map. This uses an external scanner, not the phone camera.\n" +
            "2. Import a .LAS, .LAZ, or .OBJ file, or connect the scanner with Bluetooth.\n" +
            "3. On the LiDAR screen, Place at chooses the station. The default is the HUD From station when that name is in the book. Otherwise it is the entrance.\n" +
            "4. Put outline on Plan draws the LiDAR walls on the Plan, centered on that station. Scan heading turns the file clockwise. Match leg uses the azimuth of the first main shot leaving the station. 0° keeps the file north. Shots stay as written.\n" +
            "5. Open Plan on the phone to see the lines. Erase the LiDAR lines to remove them. Putting an outline on the plan again replaces the previous one.\n" +
            "6. A file in degrees belongs on Surface Map, Aerial DEM, not on the cave plan.\n" +
            "7. On this PC, Tools -> LiDAR scanner imports OBJ, ASCII XYZ, or uncompressed LAS, or connects the same Bluetooth scanner. Put outline on plan writes the walls. Save the project so the phone and this website show the same lines. LAZ stays on the phone.",
            ["lidar", "las file", "laz file", "point cloud", "outline", "scanner", "lidar map", "scan heading", "match leg"]),
    ];
}
