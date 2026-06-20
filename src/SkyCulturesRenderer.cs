using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StellariumCatalog;

public record ConstellationSegment(int FromHip, int ToHip);
public record ConstellationName(string NativeName, string EnglishName);

public readonly record struct Star(
    int Hip,
    double RaHours,
    double DecDegrees);

public class SkyCulture {
    public string Name { get; init; } = "";
    public Dictionary<string, List<ConstellationSegment>> Constellations { get; } = new();
    public Dictionary<string, ConstellationName> ConstellationNames { get; } = new();
}

public static class SkyCulturesRenderer {
    private static  List<SkyCulture> _skyCultures = new();

    public static IReadOnlyList<SkyCulture> SkyCultures => _skyCultures;

    private static int _activeSkyCultureIndex = -1;

    public static int ActiveSkyCultureIndex {
        get => _activeSkyCultureIndex;
        set {
            if(value < -1 || value >= _skyCultures.Count)
                return;

            if(_activeSkyCultureIndex == value)
                return;

            _activeSkyCultureIndex = value;
            ResolveSegments();
        }
    }

    public static SkyCulture? ActiveSkyCulture =>
        _activeSkyCultureIndex >= 0 && _activeSkyCultureIndex < _skyCultures.Count
            ? _skyCultures[_activeSkyCultureIndex]
            : null;

    private static readonly ImColor8 white = new ImColor8(255, 255, 255, 255);

    private static readonly Dictionary<int, double3> hipToDirection = new();
    private static readonly List<ResolvedConstellationSegment> resolvedSegments = new();
    private static readonly List<ResolvedConstellationLabel> resolvedLabels = new();

    public readonly record struct ResolvedConstellationSegment(double3 A, double3 B);
    public readonly record struct ResolvedConstellationLabel(
        string Text,
        double3 Direction);

    public static void Init() {
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string skyculturesPath = Path.Combine(
            userDocs,
            "My Games",
            "Kitten Space Agency",
            "mods",
            "StellariumCatalog",
            "skycultures");

        string hygPath = Path.Combine(
            userDocs,
            "My Games",
            "Kitten Space Agency",
            "mods",
            "StellariumCatalog",
            "hyg_v42.csv");


        LoadSkyCultures(skyculturesPath);

        _activeSkyCultureIndex = 29;

        List<Star> stars = LoadHygStars(hygPath);
        SetStars(stars);
        ResolveSegments();
    }

    public static void Draw(ImDrawListPtr draw_list, Camera camera, double3 center, double radius, bool showAsterisms, bool showAsterismNames) {
        if(showAsterisms)
            DrawAsterisms(draw_list, camera, center, radius);

        if(showAsterismNames)
            DrawAsterismNames(draw_list, camera, center, radius);
    }


    public static void DrawAsterisms(ImDrawListPtr draw_list, Camera camera, double3 center, double radius) {
        foreach(ResolvedConstellationSegment segment in resolvedSegments) {
            double3 mirroredA = StarDirectionConverter.MirrorForGameSkybox(segment.A);
            double3 mirroredB = StarDirectionConverter.MirrorForGameSkybox(segment.B);

            double3 correctedA = StarDirectionConverter.RotateConstellationToGameSky(mirroredA);
            double3 correctedB = StarDirectionConverter.RotateConstellationToGameSky(mirroredB);

            double3 a = center + correctedA * radius;
            double3 b = center + correctedB * radius;


            ImDrawListExtensions.AddLine(
                draw_list,
                camera.EgoToScreen(a),
                camera.EgoToScreen(b),
                white,
                2f);
        }
    }

    public static void DrawAsterismNames(
        ImDrawListPtr draw_list,
        Camera camera,
        double3 center,
        double radius) {

        foreach(ResolvedConstellationLabel label in resolvedLabels) {
            double3 mirrored = StarDirectionConverter.MirrorForGameSkybox(label.Direction);
            double3 corrected = StarDirectionConverter.RotateConstellationToGameSky(mirrored);

            double3 position = center + corrected * radius;

            float2 screen = camera.EgoToScreen(position);

            ImDrawListExtensions.AddText(
                draw_list,
                screen,
                white,
                label.Text);
        }
    }

    public static void ResolveSegments() {
        resolvedSegments.Clear();
        resolvedLabels.Clear();

        SkyCulture? culture = ActiveSkyCulture;

        if(culture == null)
            return;

        foreach(KeyValuePair<string, List<ConstellationSegment>> entry in culture.Constellations) {
            string constellationId = entry.Key;
            List<ConstellationSegment> segments = entry.Value;

            HashSet<int> labelStarHips = new();

            foreach(ConstellationSegment segment in segments) {
                if(!hipToDirection.TryGetValue(segment.FromHip, out double3 a))
                    continue;

                if(!hipToDirection.TryGetValue(segment.ToHip, out double3 b))
                    continue;

                resolvedSegments.Add(new ResolvedConstellationSegment(a, b));

                labelStarHips.Add(segment.FromHip);
                labelStarHips.Add(segment.ToHip);
            }

            if(labelStarHips.Count == 0)
                continue;

            if(!culture.ConstellationNames.TryGetValue(constellationId, out ConstellationName name))
                continue;

            double3 sum = default;
            int count = 0;

            foreach(int hip in labelStarHips) {
                if(!hipToDirection.TryGetValue(hip, out double3 direction))
                    continue;

                sum += direction;
                count++;
            }

            if(count == 0)
                continue;

            double length = Math.Sqrt(
                sum.X * sum.X +
                sum.Y * sum.Y +
                sum.Z * sum.Z);

            if(length <= 0.000001)
                continue;

            double3 labelDirection = sum / length;

            string labelText = !string.IsNullOrWhiteSpace(name.EnglishName)
                ? name.EnglishName
                : name.NativeName;

            resolvedLabels.Add(new ResolvedConstellationLabel(
                labelText,
                labelDirection));
        }
    }

    public static void SetStars(IEnumerable<Star> stars) {
        hipToDirection.Clear();

        foreach(Star star in stars) {
            if(star.Hip <= 0)
                continue;

            hipToDirection[star.Hip] =
                StarDirectionConverter.RaDecToDirection(
                    star.RaHours,
                    star.DecDegrees);
        }
    }

    private static List<Star> LoadHygStars(string path) {
        List<Star> stars = new();

        if(!File.Exists(path))
            return stars;

        using StreamReader reader = new StreamReader(path);

        string? headerLine = reader.ReadLine();

        if(headerLine == null)
            return stars;

        string[] headers = SplitCsvLine(headerLine);

        int hipIndex = Array.IndexOf(headers, "hip");
        int raIndex = Array.IndexOf(headers, "ra");
        int decIndex = Array.IndexOf(headers, "dec");

        if(hipIndex < 0 || raIndex < 0 || decIndex < 0)
            return stars;

        while(reader.ReadLine() is string rawLine) {
            if(string.IsNullOrWhiteSpace(rawLine))
                continue;

            string[] parts = SplitCsvLine(rawLine);

            int maxNeededIndex = Math.Max(hipIndex, Math.Max(raIndex, decIndex));

            if(parts.Length <= maxNeededIndex)
                continue;

            string hipText = parts[hipIndex];

            if(string.IsNullOrWhiteSpace(hipText))
                continue;

            if(!int.TryParse(
                hipText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int hip)) {
                continue;
            }

            if(hip <= 0)
                continue;

            if(!double.TryParse(
                parts[raIndex],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double raHours)) {
                continue;
            }

            if(!double.TryParse(
                parts[decIndex],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double decDegrees)) {
                continue;
            }

            stars.Add(new Star(hip, raHours, decDegrees));
        }

        return stars;
    }

    private static string[] SplitCsvLine(string line) {
        List<string> fields = new();
        int start = 0;
        bool inQuotes = false;

        for(int i = 0; i < line.Length; i++) {
            char c = line[i];

            if(c == '"') {
                if(inQuotes && i + 1 < line.Length && line[i + 1] == '"') {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if(c == ',' && !inQuotes) {
                fields.Add(UnquoteCsvField(line.Substring(start, i - start)));
                start = i + 1;
            }
        }

        fields.Add(UnquoteCsvField(line.Substring(start)));

        return fields.ToArray();
    }

    private static string UnquoteCsvField(string field) {
        field = field.Trim();

        if(field.Length >= 2 && field[0] == '"' && field[^1] == '"') {
            field = field.Substring(1, field.Length - 2);
            field = field.Replace("\"\"", "\"");
        }

        return field;
    }

    public static void LoadSkyCultures(string path) {
        _skyCultures.Clear();

        if(!Directory.Exists(path))
            return;

        foreach(string cultureDir in Directory.EnumerateDirectories(path)) {
            string shipFile = Path.Combine(cultureDir, "constellationship.fab");

            if(!File.Exists(shipFile))
                continue;

            string name = Path.GetFileName(cultureDir);
            name = name.Replace('_', ' ');
            name = char.ToUpper(name[0]) + name.Substring(1);

            SkyCulture culture = new SkyCulture {
                Name = name
            };

            LoadConstellationShipFile(shipFile, culture);

            string namesFile = Path.Combine(cultureDir, "constellation_names.eng.fab");

            if(!File.Exists(namesFile)) {
                namesFile = Directory
                    .EnumerateFiles(cultureDir, "constellation_names.*.fab")
                    .FirstOrDefault();
            }

            if(namesFile != null)
                LoadConstellationNamesFile(namesFile, culture);

            _skyCultures.Add(culture);
        }
    }

    private static void LoadConstellationShipFile(string file, SkyCulture culture) {
        foreach(string rawLine in File.ReadLines(file)) {
            string line = rawLine.Trim();

            if(line.Length == 0)
                continue;

            if(line.StartsWith("#"))
                continue;

            string[] parts = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            if(parts.Length < 2)
                continue;

            string abbrev = parts[0];

            if(!int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int segmentCount)) {
                continue;
            }

            int expectedStarIdCount = segmentCount * 2;

            if(parts.Length < 2 + expectedStarIdCount)
                continue;

            List<ConstellationSegment> segments = new();

            for(int i = 0; i < segmentCount; i++) {
                int fromIndex = 2 + i * 2;
                int toIndex = fromIndex + 1;

                if(!int.TryParse(
                    parts[fromIndex],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int fromHip)) {
                    continue;
                }

                if(!int.TryParse(
                    parts[toIndex],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int toHip)) {
                    continue;
                }

                segments.Add(new ConstellationSegment(fromHip, toHip));
            }

            culture.Constellations[abbrev] = segments;
        }
    }

    private static readonly Regex _constellationNameRegex = new(
        @"^\s*(\S+)\s+""((?:[^""\\]|\\.)*)""\s+_\(""((?:[^""\\]|\\.)*)""\)",
        RegexOptions.Compiled);

    private static void LoadConstellationNamesFile(string file, SkyCulture culture) {
        foreach(string rawLine in File.ReadLines(file)) {
            string line = rawLine.Trim();

            if(line.Length == 0)
                continue;

            if(line.StartsWith("#"))
                continue;

            Match match = _constellationNameRegex.Match(line);

            if(!match.Success)
                continue;

            string id = match.Groups[1].Value;
            string nativeName = UnescapeFabString(match.Groups[2].Value);
            string englishName = UnescapeFabString(match.Groups[3].Value);

            culture.ConstellationNames[id] = new ConstellationName(
                nativeName,
                englishName);
        }
    }

    private static string UnescapeFabString(string text) {
        return text
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t");
    }
}