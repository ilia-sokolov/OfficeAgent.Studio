using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OfficeAgent.Studio;

/// <summary>A portable, reviewable design-system artifact produced by the design agent.</summary>
public sealed record DesignSystemPlan
{
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    [JsonPropertyName("wordmark")]
    public required string Wordmark { get; init; }

    [JsonPropertyName("eyebrowUppercase")]
    public required bool EyebrowUppercase { get; init; }

    [JsonPropertyName("palette")]
    public required DesignPalettePlan Palette { get; init; }

    [JsonPropertyName("typography")]
    public required DesignTypographyPlan Typography { get; init; }

    [JsonPropertyName("geometry")]
    public required DesignGeometryPlan Geometry { get; init; }

    [JsonPropertyName("backdrop")]
    public required DesignBackdropPlan Backdrop { get; init; }
}

public sealed record DesignPalettePlan
{
    public required string Ink { get; init; }
    public required string InkDeep { get; init; }
    public required string Paper { get; init; }
    public required string Wash { get; init; }
    public required string WashDeep { get; init; }
    public required string Body { get; init; }
    public required string Muted { get; init; }
    public required string MutedReverse { get; init; }
    public required string Accent { get; init; }
    public required string AccentText { get; init; }
    public required string AccentReverse { get; init; }
    public required string Reverse { get; init; }
}

public sealed record DesignTypographyPlan
{
    public required string DisplayFont { get; init; }
    public required string TextFont { get; init; }
    public required int DisplaySize { get; init; }
    public required int TitleSize { get; init; }
    public required int SubtitleSize { get; init; }
    public required int BodySize { get; init; }
    public required int CaptionSize { get; init; }
    public required int StatSize { get; init; }
    public required int DocumentTitleSize { get; init; }
    public required int DocumentHeadingSize { get; init; }
    public required int DocumentSubheadingSize { get; init; }
    public required int DocumentBodySize { get; init; }
    public required int DocumentQuoteSize { get; init; }
    public required int DocumentCaptionSize { get; init; }
}

public sealed record DesignGeometryPlan
{
    public required int Margin { get; init; }
    public required int DocumentMeasureInset { get; init; }
    public required int DocumentIndent { get; init; }
    public required int RuleHeight { get; init; }
}

public sealed record DesignBackdropPlan
{
    public required double PageOpacity { get; init; }
    public required double CoverLift { get; init; }
}

/// <summary>Validates generated systems before they can reach a composer.</summary>
internal static partial class DesignSystemPlanValidator
{
    private static readonly HashSet<string> PortableFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arial", "Aptos", "Calibri", "Cambria", "Georgia", "Tahoma",
        "Times New Roman", "Trebuchet MS", "Verdana"
    };

    internal static DesignSystemPlan NormalizeAndValidate(DesignSystemPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Palette is null || plan.Typography is null || plan.Geometry is null || plan.Backdrop is null)
            throw new InvalidOperationException("The generated design system is missing a required section.");

        var palette = plan.Palette with
        {
            Ink = Hex(plan.Palette.Ink, "palette.ink"),
            InkDeep = Hex(plan.Palette.InkDeep, "palette.inkDeep"),
            Paper = Hex(plan.Palette.Paper, "palette.paper"),
            Wash = Hex(plan.Palette.Wash, "palette.wash"),
            WashDeep = Hex(plan.Palette.WashDeep, "palette.washDeep"),
            Body = Hex(plan.Palette.Body, "palette.body"),
            Muted = Hex(plan.Palette.Muted, "palette.muted"),
            MutedReverse = Hex(plan.Palette.MutedReverse, "palette.mutedReverse"),
            Accent = Hex(plan.Palette.Accent, "palette.accent"),
            AccentText = Hex(plan.Palette.AccentText, "palette.accentText"),
            AccentReverse = Hex(plan.Palette.AccentReverse, "palette.accentReverse"),
            Reverse = Hex(plan.Palette.Reverse, "palette.reverse")
        };

        var typography = plan.Typography with
        {
            DisplayFont = Font(plan.Typography.DisplayFont, "typography.displayFont"),
            TextFont = Font(plan.Typography.TextFont, "typography.textFont")
        };

        var normalized = plan with
        {
            Name = Text(plan.Name, "name", 2, 40),
            Rationale = Text(plan.Rationale, "rationale", 20, 600),
            Wordmark = Text(plan.Wordmark, "wordmark", 1, 40),
            Palette = palette,
            Typography = typography
        };

        if (normalized.SchemaVersion != 1)
            throw new InvalidOperationException(
                $"Unsupported design-system schema version {normalized.SchemaVersion}; expected 1.");

        var errors = new List<string>();

        Range(errors, typography.DisplaySize, 64, 144, "displaySize");
        Range(errors, typography.TitleSize, 48, 96, "titleSize");
        Range(errors, typography.SubtitleSize, 28, 56, "subtitleSize");
        Range(errors, typography.BodySize, 20, 36, "bodySize");
        Range(errors, typography.CaptionSize, 16, 24, "captionSize");
        Range(errors, typography.StatSize, 72, 144, "statSize");
        Range(errors, typography.DocumentTitleSize, 48, 96, "documentTitleSize");
        Range(errors, typography.DocumentHeadingSize, 28, 56, "documentHeadingSize");
        Range(errors, typography.DocumentSubheadingSize, 20, 36, "documentSubheadingSize");
        Range(errors, typography.DocumentBodySize, 18, 26, "documentBodySize");
        Range(errors, typography.DocumentQuoteSize, 22, 40, "documentQuoteSize");
        Range(errors, typography.DocumentCaptionSize, 16, 22, "documentCaptionSize");
        Range(errors, normalized.Geometry.Margin, 48, 160, "margin");
        Range(errors, normalized.Geometry.DocumentMeasureInset, 1_200, 3_200, "documentMeasureInset");
        Range(errors, normalized.Geometry.DocumentIndent, 240, 720, "documentIndent");
        Range(errors, normalized.Geometry.RuleHeight, 4, 12, "ruleHeight");
        Range(errors, normalized.Backdrop.PageOpacity, 0.20, 0.75, "pageOpacity");
        Range(errors, normalized.Backdrop.CoverLift, 0.04, 0.18, "coverLift");

        if (!(typography.DisplaySize > typography.TitleSize
              && typography.TitleSize > typography.SubtitleSize
              && typography.SubtitleSize > typography.BodySize
              && typography.BodySize > typography.CaptionSize))
        {
            errors.Add("slide type sizes must descend from display to title, subtitle, body and caption");
        }

        if (!(typography.DocumentTitleSize > typography.DocumentHeadingSize
              && typography.DocumentHeadingSize > typography.DocumentSubheadingSize
              && typography.DocumentSubheadingSize > typography.DocumentBodySize
              && typography.DocumentBodySize > typography.DocumentCaptionSize))
        {
            errors.Add("document type sizes must descend from title to heading, subheading, body and caption");
        }

        if (typography.StatSize < typography.TitleSize)
            errors.Add("statSize must be at least titleSize");
        if (typography.DocumentQuoteSize <= typography.DocumentBodySize)
            errors.Add("documentQuoteSize must be greater than documentBodySize");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Generated design system is invalid: " + string.Join("; ", errors) + ".");

        var design = ToDesignSystem(normalized);

        Contrast(errors, design.Body, design.Paper, 4.5, "body on paper");
        Contrast(errors, design.Body, design.RenderedWash, 4.5, "body on rendered wash");
        Contrast(errors, design.Muted, design.Paper, 4.5, "muted text on paper");
        Contrast(errors, design.Muted, design.RenderedWash, 4.5, "muted text on rendered wash");
        Contrast(errors, design.AccentText, design.Paper, 4.5, "accent text on paper");
        Contrast(errors, design.AccentText, design.RenderedWash, 4.5, "accent text on rendered wash");
        Contrast(errors, design.Reverse, design.Ink, 4.5, "reverse text on ink");
        Contrast(errors, design.MutedReverse, design.Ink, 4.5, "reverse muted text on ink");
        Contrast(errors, design.MutedReverse, design.InkDeep, 4.5, "reverse muted text on deep ink");
        Contrast(errors, design.AccentReverse, design.CoverLightest, 4.5, "reverse accent on lifted cover");
        Contrast(errors, design.AccentReverse, design.Ink, 4.5, "reverse accent on ink");
        Contrast(errors, design.Accent, design.Wash, 3.0, "large accent text on wash");
        Contrast(errors, design.Ink, design.Paper, 4.5, "ink on paper");

        if (DesignSystem.Contrast(design.Paper, "000000") < 12.0)
            errors.Add("paper must remain a light ground");
        if (DesignSystem.Contrast(design.Wash, "000000") < 10.0)
            errors.Add("wash must remain a light ground");
        if (DesignSystem.Contrast(design.Ink, "FFFFFF") < 7.0)
            errors.Add("ink must remain a dark cover colour");

        if (Lightness(design.MutedReverse) <= Lightness(design.Muted))
            errors.Add("mutedReverse must be lighter than muted");
        if (Lightness(design.AccentText) > Lightness(design.Accent))
            errors.Add("accentText must not be lighter than accent");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Generated design system is invalid: " + string.Join("; ", errors) + ".");

        return normalized;
    }

    internal static DesignSystem ToDesignSystem(DesignSystemPlan plan)
    {
        var palette = plan.Palette;
        var type = plan.Typography;
        return new DesignSystem
        {
            Ink = palette.Ink,
            InkDeep = palette.InkDeep,
            Paper = palette.Paper,
            Wash = palette.Wash,
            WashDeep = palette.WashDeep,
            Body = palette.Body,
            Muted = palette.Muted,
            MutedReverse = palette.MutedReverse,
            Accent = palette.Accent,
            AccentText = palette.AccentText,
            AccentReverse = palette.AccentReverse,
            Reverse = palette.Reverse,
            Wordmark = plan.Wordmark,
            EyebrowUppercase = plan.EyebrowUppercase,
            DisplayFont = type.DisplayFont,
            TextFont = type.TextFont,
            DisplaySize = type.DisplaySize,
            TitleSize = type.TitleSize,
            SubtitleSize = type.SubtitleSize,
            BodySize = type.BodySize,
            CaptionSize = type.CaptionSize,
            StatSize = type.StatSize,
            Margin = plan.Geometry.Margin,
            DocumentTitleSize = type.DocumentTitleSize,
            DocumentHeadingSize = type.DocumentHeadingSize,
            DocumentSubheadingSize = type.DocumentSubheadingSize,
            DocumentBodySize = type.DocumentBodySize,
            DocumentQuoteSize = type.DocumentQuoteSize,
            DocumentCaptionSize = type.DocumentCaptionSize,
            DocumentMeasureInset = plan.Geometry.DocumentMeasureInset,
            DocumentIndent = plan.Geometry.DocumentIndent,
            RuleHeight = plan.Geometry.RuleHeight,
            PageBackdropOpacity = plan.Backdrop.PageOpacity,
            CoverLift = plan.Backdrop.CoverLift
        };
    }

    private static string Hex(string? value, string name)
    {
        var text = Text(value, name, 6, 7).TrimStart('#').ToUpperInvariant();
        if (!HexColor().IsMatch(text))
            throw new InvalidOperationException($"{name} must be a six-digit hexadecimal colour.");
        return text;
    }

    private static string Font(string? value, string name)
    {
        var text = Text(value, name, 2, 40);
        if (!PortableFonts.TryGetValue(text, out var canonical))
            throw new InvalidOperationException(
                $"{name} '{text}' is not portable. Choose one of: {string.Join(", ", PortableFonts.Order())}.");
        return canonical;
    }

    private static string Text(string? value, string name, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is empty.");
        var text = value.Trim();
        if (text.Length < minimum || text.Length > maximum || text.Any(char.IsControl))
            throw new InvalidOperationException($"{name} must contain {minimum} to {maximum} printable characters.");
        return text;
    }

    private static void Range(List<string> errors, int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
            errors.Add($"{name} must be between {minimum} and {maximum}");
    }

    private static void Range(List<string> errors, double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            errors.Add($"{name} must be between {minimum:0.##} and {maximum:0.##}");
    }

    private static void Contrast(
        List<string> errors, string foreground, string background, double threshold, string name)
    {
        var ratio = DesignSystem.Contrast(foreground, background);
        if (ratio < threshold)
            errors.Add($"{name} is {ratio:0.00}:1, below {threshold:0.0}:1");
    }

    private static double Lightness(string color) => DesignSystem.Contrast(color, "000000");

    [GeneratedRegex("^[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}

/// <summary>Atomic persistence and loading for generated design-system artifacts.</summary>
internal static class DesignSystemFiles
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static async Task<string> SaveAsync(
        DesignSystemPlan plan,
        string outputRoot,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var normalized = DesignSystemPlanValidator.NormalizeAndValidate(plan);
        if (!string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("A design-system output must be a bare .json filename.");
        }

        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        var finalPath = Path.Combine(root, fileName);
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
            throw new IOException($"A design-system artifact named '{fileName}' already exists.");
        var temporaryPath = Path.Combine(root, $"partial-{Guid.NewGuid():N}.json");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16_384, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, Json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath, overwrite: false);
            return finalPath;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception) { /* Cleanup must not hide serialization or publication failure. */ }
        }
    }

    internal static DesignSystem Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The design-system file path is empty.");

        var fullPath = Path.GetFullPath(path.Trim());
        try
        {
            var length = new FileInfo(fullPath).Length;
            if (length > 128 * 1024)
                throw new InvalidOperationException("The design-system file is larger than 128 KiB.");
            var plan = JsonSerializer.Deserialize<DesignSystemPlan>(File.ReadAllText(fullPath), Json)
                ?? throw new JsonException("The file contained no design system.");
            var normalized = DesignSystemPlanValidator.NormalizeAndValidate(plan);
            return DesignSystemPlanValidator.ToDesignSystem(normalized);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                                      or InvalidOperationException)
        {
            throw new StudioException(
                $"Could not load design system '{fullPath}': {error.Message}",
                "Generate a new file with the design-system command or correct the invalid values.",
                error);
        }
    }

    internal static DesignSystem Resolve(Func<string, string?> setting)
    {
        var file = setting("OFFICEAGENT_STUDIO_BRAND_FILE");
        var name = setting("OFFICEAGENT_STUDIO_BRAND");
        if (!string.IsNullOrWhiteSpace(file) && !string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Set either OFFICEAGENT_STUDIO_BRAND_FILE or OFFICEAGENT_STUDIO_BRAND, not both.");

        return string.IsNullOrWhiteSpace(file) ? DesignSystem.ByName(name) : Load(file);
    }
}
