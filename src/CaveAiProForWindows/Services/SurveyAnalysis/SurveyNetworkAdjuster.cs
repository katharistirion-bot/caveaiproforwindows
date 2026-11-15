using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SurveyAnalysis;

/// <summary>
/// One least-squares solve for the whole traverse. Each free station is the weighted
/// meeting point of the legs that touch it. Fixed stations and #fix coordinates stay put.
/// Equated names share one unknown. This replaces adjusting one loop at a time.
/// </summary>
internal static class SurveyNetworkAdjuster
{
    public static Dictionary<string, (float X, float Y, float Z)> Solve(CaveProjectDocument project)
    {
        var initial = SurveyStationGeometry.CalculatePlanCoordinates(
            project.Shots,
            (float)project.Alt,
            planStationPositionOverrides: null,
            project.FixedStationPositions,
            project.StationEquates);

        var coords = new Dictionary<string, (float X, float Y, float Z)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in initial)
            coords[kv.Key] = (kv.Value.X, kv.Value.Y, kv.Value.Z);

        var legs = project.Shots.Where(s => s.IsNetworkLeg && s.Distance > 1e-4f).ToList();
        if (legs.Count == 0)
            return coords;

        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Find(string name)
        {
            var key = name.Trim();
            if (!parent.TryGetValue(key, out var p))
            {
                parent[key] = key;
                return key;
            }

            var root = key;
            while (!parent[root].Equals(root, StringComparison.OrdinalIgnoreCase))
                root = parent[root];
            var i = key;
            while (!i.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                var next = parent[i];
                parent[i] = root;
                i = next;
            }

            return root;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (!ra.Equals(rb, StringComparison.OrdinalIgnoreCase))
                parent[ra] = rb;
        }

        if (project.StationEquates != null)
        {
            foreach (var eq in project.StationEquates)
            {
                if (string.IsNullOrWhiteSpace(eq.StationA) || string.IsNullOrWhiteSpace(eq.StationB))
                    continue;
                Union(eq.StationA, eq.StationB);
            }
        }

        var canonPos = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in coords)
        {
            var c = Find(kv.Key);
            if (!canonPos.ContainsKey(c))
                canonPos[c] = kv.Value;
        }

        if (project.FixedStationPositions != null)
        {
            foreach (var kv in project.FixedStationPositions)
                canonPos[Find(kv.Key)] = (kv.Value.X, kv.Value.Y, kv.Value.Z);
        }

        var fixedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (project.FixedStations != null)
        {
            foreach (var name in project.FixedStations)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    fixedNames.Add(Find(name));
            }
        }

        if (project.FixedStationPositions != null)
        {
            foreach (var name in project.FixedStationPositions.Keys)
                fixedNames.Add(Find(name));
        }

        if (fixedNames.Count == 0)
        {
            var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var leg in legs)
                incoming.Add(Find(leg.ToStation));
            var start = legs.FirstOrDefault(leg => !incoming.Contains(Find(leg.FromStation)));
            fixedNames.Add(Find((start ?? legs[0]).FromStation));
        }

        var observations = new List<(string From, string To, double Dx, double Dy, double Dz, double W)>();
        foreach (var leg in legs)
        {
            var from = Find(leg.FromStation);
            var to = Find(leg.ToStation);
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
                continue;
            var (dx, dy, dz) = SurveyLegGeometry.Delta(leg);
            var weight = 1.0 / Math.Pow(Math.Max(leg.Distance, 0.05), 2);
            if (SurveyLegFlags.IsHeld(leg.LegFlags))
                weight *= 1_000_000.0;
            observations.Add((from, to, dx, dy, dz, weight));
            if (!canonPos.ContainsKey(from) && coords.TryGetValue(leg.FromStation, out var fc))
                canonPos[from] = fc;
            if (!canonPos.ContainsKey(to) && coords.TryGetValue(leg.ToStation, out var tc))
                canonPos[to] = tc;
        }

        var settled = false;
        for (var iter = 0; iter < 400; iter++)
        {
            var maxMove = 0.0;
            foreach (var station in canonPos.Keys.ToList())
            {
                if (fixedNames.Contains(station))
                    continue;
                double sw = 0, sx = 0, sy = 0, sz = 0;
                foreach (var o in observations)
                {
                    if (o.To.Equals(station, StringComparison.OrdinalIgnoreCase) &&
                        canonPos.TryGetValue(o.From, out var fromPos))
                    {
                        sw += o.W;
                        sx += o.W * (fromPos.X + o.Dx);
                        sy += o.W * (fromPos.Y + o.Dy);
                        sz += o.W * (fromPos.Z + o.Dz);
                    }
                    else if (o.From.Equals(station, StringComparison.OrdinalIgnoreCase) &&
                             canonPos.TryGetValue(o.To, out var toPos))
                    {
                        sw += o.W;
                        sx += o.W * (toPos.X - o.Dx);
                        sy += o.W * (toPos.Y - o.Dy);
                        sz += o.W * (toPos.Z - o.Dz);
                    }
                }

                if (sw < 1e-12)
                    continue;
                var nx = sx / sw;
                var ny = sy / sw;
                var nz = sz / sw;
                var cur = canonPos[station];
                maxMove = Math.Max(maxMove, Math.Abs(nx - cur.X) + Math.Abs(ny - cur.Y) + Math.Abs(nz - cur.Z));
                canonPos[station] = (nx, ny, nz);
            }

            if (maxMove < 0.0005)
            {
                settled = true;
                break;
            }
        }

        if (!settled)
            return coords;

        foreach (var key in coords.Keys.ToList())
        {
            if (canonPos.TryGetValue(Find(key), out var p))
                coords[key] = ((float)p.X, (float)p.Y, (float)p.Z);
        }

        if (project.StationEquates != null)
        {
            foreach (var eq in project.StationEquates)
            {
                foreach (var name in new[] { eq.StationA, eq.StationB })
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (canonPos.TryGetValue(Find(name), out var p))
                        coords[name.Trim()] = ((float)p.X, (float)p.Y, (float)p.Z);
                }
            }
        }

        return coords;
    }
}
