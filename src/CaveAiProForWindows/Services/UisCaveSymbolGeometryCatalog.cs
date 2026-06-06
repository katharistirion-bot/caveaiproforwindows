using System.Windows;
using System.Windows.Media;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Desktop port of Android <c>UisCaveSymbols.kt</c> — normalized line art in [-1,1] around the origin.
/// Stored Android icons use <c>__UIS__{slug}__</c>.
/// </summary>
public static class UisCaveSymbolGeometryCatalog
{
    public const string Prefix = "__UIS__";
    public const string Suffix = "__";

    private sealed record Seg(float X1, float Y1, float X2, float Y2);

    private sealed record Def(string Slug, string Label, IReadOnlyList<Seg> Lines, IReadOnlyList<IReadOnlyList<(float X, float Y)>> Closed);

    private static readonly Dictionary<string, Def> BySlug;
    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.OrdinalIgnoreCase);

    static UisCaveSymbolGeometryCatalog()
    {
        var defs = BuildDefs();
        BySlug = defs.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryParseStoredIcon(string? raw, out string slug)
    {
        slug = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (!s.StartsWith(Prefix, StringComparison.Ordinal) || !s.EndsWith(Suffix, StringComparison.Ordinal))
            return false;
        slug = s[Prefix.Length..^Suffix.Length].Trim();
        return slug.Length > 0;
    }

    public static string ToStoredIcon(string slug) => $"{Prefix}{slug}{Suffix}";

    public static bool TryGetLabel(string slug, out string label)
    {
        if (BySlug.TryGetValue(slug, out var def))
        {
            label = def.Label;
            return true;
        }

        label = "";
        return false;
    }

    public static IReadOnlyCollection<string> AllSlugs => BySlug.Keys;

    public static bool TryGetGeometry(string slug, out Geometry geometry)
    {
        if (!BySlug.ContainsKey(slug))
        {
            geometry = Geometry.Empty;
            return false;
        }

        if (GeometryCache.TryGetValue(slug, out geometry!))
            return true;

        geometry = BuildGeometry(BySlug[slug]);
        GeometryCache[slug] = geometry;
        return true;
    }

    private static Geometry BuildGeometry(Def def)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var poly in def.Closed)
        {
            if (poly.Count < 2)
                continue;
            var fig = new PathFigure(new Point(poly[0].X, poly[0].Y), [], true);
            for (var i = 1; i < poly.Count; i++)
                fig.Segments.Add(new LineSegment(new Point(poly[i].X, poly[i].Y), true));
            group.Children.Add(new PathGeometry([fig]));
        }

        foreach (var seg in def.Lines)
        {
            var fig = new PathFigure(new Point(seg.X1, seg.Y1), [new LineSegment(new Point(seg.X2, seg.Y2), true)], false);
            group.Children.Add(new PathGeometry([fig]));
        }

        group.Freeze();
        return group;
    }

    private static List<Def> BuildDefs()
    {
        var outList = new List<Def>();
        void Add(Def d) => outList.Add(d);

        Add(new Def("stalactite", "Stalactite (UIS)",
            [new Seg(0f, -0.85f, 0f, 0.2f), new Seg(-0.2f, -0.55f, 0f, -0.15f), new Seg(0.2f, -0.55f, 0f, -0.15f), new Seg(-0.12f, -0.85f, 0.12f, -0.85f)],
            []));
        Add(new Def("stalagmite", "Stalagmite (UIS)",
            [new Seg(0f, 0.85f, 0f, -0.2f), new Seg(-0.2f, 0.55f, 0f, 0.15f), new Seg(0.2f, 0.55f, 0f, 0.15f), new Seg(-0.12f, 0.85f, 0.12f, 0.85f)],
            []));
        Add(new Def("column", "Column (UIS)",
            [new Seg(0f, -0.75f, 0f, 0.75f), new Seg(-0.18f, -0.75f, 0.18f, -0.75f), new Seg(-0.18f, 0.75f, 0.18f, 0.75f)],
            []));
        Add(new Def("curtain", "Curtain / drapery (UIS)",
            [new Seg(-0.45f, -0.7f, -0.35f, 0.65f), new Seg(-0.15f, -0.75f, -0.05f, 0.7f), new Seg(0.05f, -0.75f, 0.15f, 0.7f), new Seg(0.35f, -0.7f, 0.45f, 0.65f)],
            []));
        Add(new Def("soda_straw", "Soda straw (UIS)",
            [new Seg(-0.25f, -0.85f, -0.25f, 0.1f), new Seg(0.25f, -0.85f, 0.25f, 0.1f), new Seg(-0.25f, -0.85f, 0.25f, -0.85f)],
            []));
        Add(new Def("flowstone", "Flowstone (UIS)",
            [new Seg(-0.35f, -0.05f, 0.35f, -0.2f), new Seg(-0.4f, 0.15f, 0.3f, 0.05f)],
            [[(-0.55f, -0.2f), (0.55f, -0.35f), (0.5f, 0.45f), (-0.5f, 0.55f)]]));
        Add(new Def("rimstone", "Rimstone / gour (UIS)", [],
            [[(-0.45f, 0.1f), (0f, -0.35f), (0.45f, 0.1f), (0f, 0.45f)]]));
        Add(new Def("helictite", "Helictite (UIS)", HelixSegments(0f, 0f, 0.65f, 5), []));
        Add(new Def("moonmilk", "Moonmilk (UIS)",
            [new Seg(-0.35f, 0f, 0.35f, 0.05f), new Seg(-0.2f, 0.18f, 0.25f, 0.2f)],
            [[(-0.5f, -0.15f), (0.5f, -0.15f), (0.45f, 0.35f), (-0.45f, 0.35f)]]));
        Add(new Def("cave_pearl", "Cave pearls (UIS)",
            [new Seg(-0.35f, -0.15f, -0.15f, 0.15f), new Seg(0f, -0.25f, 0.2f, 0.1f), new Seg(0.25f, -0.1f, 0.45f, 0.2f)],
            []));

        Add(new Def("stream", "Subterranean stream (UIS)",
            [new Seg(-0.65f, 0f, 0.65f, 0f), new Seg(0.45f, -0.12f, 0.65f, 0f), new Seg(0.45f, 0.12f, 0.65f, 0f)],
            []));
        Add(new Def("pool_water", "Water pool (UIS)", [],
            [[(-0.55f, 0f), (-0.25f, -0.35f), (0.25f, -0.35f), (0.55f, 0f), (0.25f, 0.35f), (-0.25f, 0.35f)]]));
        Add(new Def("sump", "Sump (UIS)",
            [new Seg(-0.55f, -0.15f, 0.55f, -0.15f), new Seg(-0.55f, 0.2f, 0.55f, 0.2f), new Seg(0f, -0.45f, 0f, 0.45f)],
            []));
        Add(new Def("waterfall", "Waterfall (UIS)",
            [new Seg(0f, -0.75f, 0f, 0.55f), new Seg(-0.35f, -0.35f, 0.35f, -0.35f), new Seg(-0.25f, 0f, -0.05f, 0.45f), new Seg(0.05f, 0f, 0.25f, 0.45f)],
            []));
        Add(new Def("drip", "Drip (UIS)",
            [new Seg(0f, -0.65f, 0f, 0.35f), new Seg(-0.15f, 0.35f, 0.15f, 0.35f), new Seg(0f, 0.35f, 0f, 0.55f)],
            []));

        Add(new Def("mud", "Mud (UIS)", MudDots(), []));
        Add(new Def("sand", "Sand (UIS)",
            [new Seg(-0.55f, 0.15f, 0.55f, 0.15f), new Seg(-0.55f, -0.05f, 0.55f, -0.05f), new Seg(-0.55f, -0.25f, 0.55f, -0.25f)],
            []));
        Add(new Def("pebbles", "Pebbles / gravel (UIS)",
            [new Seg(-0.4f, -0.2f, -0.2f, 0f), new Seg(0f, -0.25f, 0.2f, 0.05f), new Seg(0.35f, -0.15f, 0.5f, 0.1f), new Seg(-0.25f, 0.2f, 0.1f, 0.35f)],
            []));
        Add(new Def("clay", "Clay (UIS)",
            [new Seg(-0.2f, 0f, 0.2f, 0.05f), new Seg(-0.15f, 0.12f, 0.25f, 0.15f)],
            [[(-0.5f, -0.25f), (0.5f, -0.2f), (0.45f, 0.3f), (-0.45f, 0.25f)]]));

        Add(new Def("pit", "Pitch / pit (UIS)",
            [new Seg(-0.45f, -0.45f, 0.45f, -0.45f), new Seg(-0.45f, -0.45f, 0f, 0.55f), new Seg(0.45f, -0.45f, 0f, 0.55f)],
            []));
        Add(new Def("aven", "Aven / shaft up (UIS)",
            [new Seg(-0.45f, 0.45f, 0.45f, 0.45f), new Seg(-0.45f, 0.45f, 0f, -0.55f), new Seg(0.45f, 0.45f, 0f, -0.55f)],
            []));
        Add(new Def("boulder", "Boulder (UIS)", [],
            [[(-0.35f, 0.45f), (-0.55f, -0.05f), (-0.15f, -0.55f), (0.35f, -0.35f), (0.5f, 0.15f), (0.15f, 0.55f)]]));
        Add(new Def("breakdown", "Breakdown (UIS)",
            [new Seg(-0.55f, -0.1f, -0.1f, 0.45f), new Seg(-0.2f, -0.45f, 0.45f, 0.05f), new Seg(0.1f, -0.35f, 0.55f, 0.35f)],
            []));
        Add(new Def("choke", "Choke (UIS)",
            [new Seg(-0.35f, -0.1f, 0.35f, 0.1f), new Seg(-0.2f, 0.1f, 0.2f, -0.1f)],
            [[(-0.55f, -0.35f), (0.55f, -0.35f), (0.35f, 0.35f), (-0.35f, 0.35f)]]));
        Add(new Def("crawl", "Crawl / low passage (UIS)",
            [new Seg(-0.65f, -0.15f, 0.65f, -0.15f), new Seg(-0.65f, 0.15f, 0.65f, 0.15f)],
            []));
        Add(new Def("step", "Floor step (UIS)",
            [new Seg(-0.55f, 0.1f, 0.15f, -0.45f), new Seg(0.15f, -0.45f, 0.55f, 0.15f)],
            []));
        Add(new Def("gradient_up", "Gradient uphill (UIS)",
            [new Seg(-0.5f, 0.25f, 0.5f, -0.25f), new Seg(0.2f, -0.35f, 0.5f, -0.25f), new Seg(0.5f, -0.25f, 0.4f, 0f)],
            []));
        Add(new Def("gradient_down", "Gradient downhill (UIS)",
            [new Seg(-0.5f, -0.25f, 0.5f, 0.25f), new Seg(0.2f, 0.35f, 0.5f, 0.25f), new Seg(0.5f, 0.25f, 0.4f, 0f)],
            []));

        Add(new Def("fixed_rope", "Fixed rope (UIS)",
            [new Seg(0f, -0.75f, 0f, 0.65f), new Seg(-0.15f, -0.45f, 0.15f, -0.25f), new Seg(-0.12f, -0.1f, 0.12f, 0.1f), new Seg(-0.15f, 0.25f, 0.15f, 0.45f)],
            []));
        Add(new Def("ladder", "Ladder (UIS)",
            [new Seg(-0.25f, -0.7f, -0.25f, 0.7f), new Seg(0.25f, -0.7f, 0.25f, 0.7f), new Seg(-0.25f, -0.4f, 0.25f, -0.4f), new Seg(-0.25f, 0f, 0.25f, 0f), new Seg(-0.25f, 0.4f, 0.25f, 0.4f)],
            []));

        Add(new Def("air_draft", "Air draft (UIS)",
            [new Seg(-0.55f, 0f, 0.55f, 0f), new Seg(-0.35f, -0.35f, -0.55f, 0f), new Seg(-0.35f, 0.35f, -0.55f, 0f), new Seg(0.35f, -0.35f, 0.55f, 0f), new Seg(0.35f, 0.35f, 0.55f, 0f)],
            []));
        Add(new Def("guano", "Bat guano (UIS)",
            [new Seg(-0.4f, -0.2f, 0.35f, 0.25f), new Seg(0.1f, -0.35f, 0.45f, 0.1f), new Seg(-0.25f, 0.15f, 0.05f, 0.45f)],
            []));
        Add(new Def("bones", "Bones / archaeology (UIS)",
            [new Seg(-0.45f, 0.35f, 0.35f, -0.35f), new Seg(-0.15f, -0.15f, 0.15f, 0.15f), new Seg(-0.35f, -0.2f, -0.1f, 0.1f), new Seg(0.1f, -0.1f, 0.35f, 0.2f)],
            []));
        Add(new Def("wood_debris", "Wood / roots (UIS)",
            [new Seg(0f, -0.55f, 0f, 0.45f), new Seg(-0.35f, -0.15f, 0.35f, 0.05f), new Seg(-0.2f, 0.25f, 0.15f, 0.5f)],
            []));
        Add(new Def("crystal", "Crystals (UIS)",
            [new Seg(0f, -0.2f, 0f, 0.35f), new Seg(-0.25f, 0.05f, 0.25f, 0.05f)],
            [[(0f, -0.55f), (0.45f, 0.2f), (-0.45f, 0.2f)]]));
        Add(new Def("ice", "Ice (UIS)", [],
            [[(-0.5f, -0.1f), (0f, -0.55f), (0.5f, -0.1f), (0.35f, 0.45f), (-0.35f, 0.45f)]]));
        Add(new Def("salt_crust", "Salt crust (UIS)",
            [new Seg(-0.55f, -0.2f, 0.55f, 0.2f), new Seg(-0.45f, 0.15f, 0.45f, -0.15f), new Seg(0f, -0.45f, 0f, 0.45f)],
            []));
        Add(new Def("raft", "Calcite raft (UIS)",
            [new Seg(-0.55f, 0f, 0.55f, 0f), new Seg(-0.35f, -0.2f, 0.35f, -0.2f), new Seg(-0.35f, 0.2f, 0.35f, 0.2f)],
            []));

        return outList;
    }

    private static List<Seg> HelixSegments(float cx, float cy, float r, int n)
    {
        var segs = new List<Seg>(n);
        for (var i = 0; i < n; i++)
        {
            var t1 = (float)(i * Math.PI * 1.2 / n);
            var t2 = (float)((i + 1) * Math.PI * 1.2 / n);
            segs.Add(new Seg(
                cx + r * MathF.Cos(t1), cy + r * MathF.Sin(t1),
                cx + r * 0.85f * MathF.Cos(t2), cy + r * 0.85f * MathF.Sin(t2)));
        }

        return segs;
    }

    private static List<Seg> MudDots()
    {
        var segs = new List<Seg>();
        (float x, float y)[] pts =
        [
            (-0.35f, -0.25f), (0.1f, -0.35f), (0.4f, -0.15f),
            (-0.2f, 0.1f), (0.25f, 0.2f), (-0.15f, 0.35f), (0.35f, 0.4f),
        ];
        foreach (var (x, y) in pts)
        {
            segs.Add(new Seg(x - 0.04f, y, x + 0.04f, y));
            segs.Add(new Seg(x, y - 0.04f, x, y + 0.04f));
        }

        return segs;
    }
}
