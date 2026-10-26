using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cabbie.Samples.Tests;

public sealed class ReleasePleaseConfigTests
{
    private const string ConfigPath = ".github/release-please-config.json";
    private const string ManifestPath = ".github/.release-please-manifest.json";
    private const string WorkflowPath = ".github/workflows/release-please.yml";

    private static readonly Regex VersionMarker = new(
        @"<Version>(\d+\.\d+\.\d+)</Version>\s*<!-- x-release-please-version -->",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cabbie.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Cabbie.slnx not found above test output.");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static JsonElement RootPackage(JsonDocument config) =>
        config.RootElement.GetProperty("packages").GetProperty(".");

    [Fact]
    public void config_and_manifest_parse_as_json_objects()
    {
        using var config = JsonDocument.Parse(Read(ConfigPath));
        using var manifest = JsonDocument.Parse(Read(ManifestPath));

        Assert.Equal(JsonValueKind.Object, config.RootElement.ValueKind);
        Assert.True(config.RootElement.TryGetProperty("packages", out _));
        Assert.Equal(JsonValueKind.Object, manifest.RootElement.ValueKind);
    }

    [Fact]
    public void first_release_bootstraps_at_0_1_0()
    {
        using var config = JsonDocument.Parse(Read(ConfigPath));
        using var manifest = JsonDocument.Parse(Read(ManifestPath));

        Assert.Empty(manifest.RootElement.EnumerateObject());
        var package = RootPackage(config);
        Assert.Equal("0.1.0", package.GetProperty("initial-version").GetString());
        Assert.Equal("simple", package.GetProperty("release-type").GetString());
    }

    [Fact]
    public void bootstrap_sha_is_a_full_commit_sha()
    {
        using var config = JsonDocument.Parse(Read(ConfigPath));

        var sha = config.RootElement.GetProperty("bootstrap-sha").GetString();

        Assert.NotNull(sha);
        Assert.Matches(FullSha, sha);
    }

    [Fact]
    public void workflow_points_at_the_checked_in_config_and_manifest()
    {
        var workflow = Read(WorkflowPath);

        Assert.Contains("config-file: " + ConfigPath, workflow, StringComparison.Ordinal);
        Assert.Contains("manifest-file: " + ManifestPath, workflow, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), ConfigPath)));
        Assert.True(File.Exists(Path.Combine(RepoRoot(), ManifestPath)));
    }

    [Fact]
    public void every_extra_file_exists_and_carries_the_version_marker()
    {
        using var config = JsonDocument.Parse(Read(ConfigPath));
        var package = RootPackage(config);
        var initial = package.GetProperty("initial-version").GetString();

        var extraFiles = package.GetProperty("extra-files").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        Assert.NotEmpty(extraFiles);
        foreach (var file in extraFiles)
        {
            var path = Path.Combine(RepoRoot(), file);
            Assert.True(File.Exists(path), string.Create(CultureInfo.InvariantCulture, $"{file} missing"));
            var match = VersionMarker.Match(File.ReadAllText(path));
            Assert.True(match.Success, string.Create(CultureInfo.InvariantCulture, $"{file} lacks version marker"));
            Assert.Equal(initial, match.Groups[1].Value);
        }
    }
}
