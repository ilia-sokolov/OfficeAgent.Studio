using OfficeAgent.Studio;
using System.Text.Json;
using Xunit;

namespace OfficeAgent.Studio.Tests;

public class GeneratedDesignSystemTests
{
    [Fact]
    public void Normalizes_and_maps_a_valid_generated_system()
    {
        var source = TestPlans.DesignSystem();
        var supplied = source with
        {
            Palette = source.Palette with { Accent = "#c8632b" },
            Typography = source.Typography with { DisplayFont = "georgia" }
        };

        var normalized = DesignSystemPlanValidator.NormalizeAndValidate(supplied);
        var design = DesignSystemPlanValidator.ToDesignSystem(normalized);

        Assert.Equal("C8632B", normalized.Palette.Accent);
        Assert.Equal("Georgia", normalized.Typography.DisplayFont);
        Assert.Equal(normalized.Wordmark, design.Wordmark);
        Assert.Equal(normalized.Geometry.Margin, design.Margin);
        Assert.True(DesignSystem.Contrast(design.Body, design.Paper) >= 4.5);
    }

    [Fact]
    public void Rejects_malformed_colours()
    {
        var source = TestPlans.DesignSystem();
        var supplied = source with { Palette = source.Palette with { Ink = "navy" } };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DesignSystemPlanValidator.NormalizeAndValidate(supplied));

        Assert.Contains("palette.ink", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unreadable_text_pairings()
    {
        var source = TestPlans.DesignSystem();
        var supplied = source with { Palette = source.Palette with { Body = source.Palette.Paper } };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DesignSystemPlanValidator.NormalizeAndValidate(supplied));

        Assert.Contains("body on paper", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_palette_that_breaks_light_and_dark_roles()
    {
        var source = TestPlans.DesignSystem();
        var supplied = source with { Palette = source.Palette with { Paper = "777777" } };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DesignSystemPlanValidator.NormalizeAndValidate(supplied));

        Assert.Contains("paper must remain a light ground", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_nonportable_fonts()
    {
        var source = TestPlans.DesignSystem();
        var supplied = source with
        {
            Typography = source.Typography with { DisplayFont = "Unlicensed Display Pro" }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DesignSystemPlanValidator.NormalizeAndValidate(supplied));

        Assert.Contains("not portable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_inverted_type_hierarchy()
    {
        var source = TestPlans.DesignSystem();
        var supplied = source with
        {
            Typography = source.Typography with { BodySize = source.Typography.TitleSize }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DesignSystemPlanValidator.NormalizeAndValidate(supplied));

        Assert.Contains("slide type sizes must descend", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Persists_and_loads_a_valid_system_without_partial_files()
    {
        var root = TemporaryRoot();
        try
        {
            var path = await DesignSystemFiles.SaveAsync(TestPlans.DesignSystem(), root, "brand.json");
            var loaded = DesignSystemFiles.Load(path);

            Assert.True(File.Exists(path));
            Assert.Equal("northwind", loaded.Wordmark);
            Assert.Equal("C8632B", loaded.Accent);
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Refuses_to_overwrite_an_artifact_and_cleans_its_temporary_file()
    {
        var root = TemporaryRoot();
        try
        {
            await DesignSystemFiles.SaveAsync(TestPlans.DesignSystem(), root, "brand.json");

            await Assert.ThrowsAsync<IOException>(() =>
                DesignSystemFiles.SaveAsync(TestPlans.DesignSystem(), root, "brand.json"));
            Assert.Single(Directory.EnumerateFiles(root, "*.json"));
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reports_malformed_artifacts_as_studio_errors()
    {
        var root = TemporaryRoot();
        try
        {
            var path = Path.Combine(root, "bad.json");
            File.WriteAllText(path, "{ not-json }");

            var error = Assert.Throws<StudioException>(() => DesignSystemFiles.Load(path));

            Assert.Contains("Could not load design system", error.Message, StringComparison.Ordinal);
            Assert.NotNull(error.Hint);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_unknown_artifact_fields()
    {
        var root = TemporaryRoot();
        try
        {
            var path = Path.Combine(root, "unknown.json");
            var json = JsonSerializer.Serialize(TestPlans.DesignSystem());
            File.WriteAllText(path, json[..^1] + ",\"unexpected\":true}");

            var error = Assert.Throws<StudioException>(() => DesignSystemFiles.Load(path));

            Assert.Contains("unexpected", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_leaves_no_artifact_or_temporary_file()
    {
        var root = TemporaryRoot();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                DesignSystemFiles.SaveAsync(
                    TestPlans.DesignSystem(), root, "cancelled.json", cancellation.Token));
            Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Resolves_a_generated_file_and_rejects_ambiguous_brand_settings()
    {
        var root = TemporaryRoot();
        try
        {
            var path = await DesignSystemFiles.SaveAsync(TestPlans.DesignSystem(), root, "brand.json");
            var loaded = DesignSystemFiles.Resolve(name =>
                name == "OFFICEAGENT_STUDIO_BRAND_FILE" ? path : null);

            Assert.Equal("northwind", loaded.Wordmark);

            var error = Assert.Throws<ArgumentException>(() => DesignSystemFiles.Resolve(name =>
                name switch
                {
                    "OFFICEAGENT_STUDIO_BRAND_FILE" => path,
                    "OFFICEAGENT_STUDIO_BRAND" => "meridian",
                    _ => null
                }));
            Assert.Contains("either", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "officeagent-studio-design-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
