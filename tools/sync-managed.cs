#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property ImplicitUsings=enable
#:property Nullable=enable

using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

var opts = ParseArgs(args);
var repoRoot = Directory.GetCurrentDirectory();

var ignoreListPath = ResolveIgnoreListPath(opts, repoRoot);
var outDir = ResolveOutputDirectory(opts, repoRoot);

Directory.CreateDirectory(outDir);
CleanDestinationFolder(outDir, repoRoot);

var managedPath = ResolveValheimManagedPath(opts);
EnsureDirectoryExists(managedPath, "Managed folder");

CopyAllExceptListedFiles(
    listPath: ignoreListPath,
    sourceDir: managedPath,
    outDir: outDir,
    label: "Valheim Managed");

await SyncRequiredNuGetDllsAsync(opts, outDir);

Console.WriteLine($"Done. Output: {outDir}");
return 0;

static string ResolveIgnoreListPath(Options opts, string repoRoot) =>
    Path.GetFullPath(opts.ListPath ?? Path.Combine(repoRoot, "ignore-managed.md"));

static string ResolveOutputDirectory(Options opts, string repoRoot) =>
    Path.GetFullPath(opts.OutDir ?? Path.Combine(repoRoot, "lib", "net46"));

static string ResolveValheimManagedPath(Options opts)
{
    var managedPath =
        FirstNonEmpty(
            opts.ManagedPath,
            Environment.GetEnvironmentVariable("VALHEIM_MANAGED"))
        ?? FindValheimManagedPath(opts.SteamPath);

    return NormalizePathCasingIfNeeded(managedPath);
}

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var v in values)
        if (!string.IsNullOrWhiteSpace(v))
            return v;
    return null;
}

static void EnsureDirectoryExists(string path, string label)
{
    if (!Directory.Exists(path))
        throw new DirectoryNotFoundException($"{label} not found: {path}");
}

static async Task SyncRequiredNuGetDllsAsync(Options opts, string outDir)
{
    var requiredDlls = new[] { "0Harmony.dll", "BepInEx.dll" };
    var pending = new HashSet<string>(requiredDlls, StringComparer.OrdinalIgnoreCase);

    var packageIds = ResolvePackageIds(opts);
    ValidatePackageIds(packageIds);

    var serviceIndexes = ResolveServiceIndexes(opts);
    ValidateServiceIndexes(serviceIndexes);

    var includePrerelease = ShouldIncludePrerelease(opts);
    var packageBaseCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var packageId in packageIds)
    {
        if (pending.Count == 0)
            break;

        var (serviceIndex, packageBaseAddress, version) = await ResolveLatestFromAnySourceAsync(
            serviceIndexes, packageId, includePrerelease, packageBaseCache);

        var extractedRoot = await DownloadAndExtractNuGetPackageAsync(packageBaseAddress, packageId, version);

        foreach (var dll in pending.ToArray())
            if (TryCopyBestMatchFromExtractedPackage(extractedRoot, dll, outDir))
                pending.Remove(dll);

        Console.WriteLine($"Processed {packageId}/{version} from {serviceIndex}");
    }

    if (pending.Count != 0)
        throw new FileNotFoundException(
            "Could not find required DLL(s) in downloaded packages: " + string.Join(", ", pending));
}

static void ValidatePackageIds(List<string> packageIds)
{
    if (packageIds.Count == 0)
        throw new InvalidOperationException("No BepInEx package ids configured. Use defaults or pass --bepInExPackageIds.");
}

static void ValidateServiceIndexes(List<string> serviceIndexes)
{
    if (serviceIndexes.Count == 0)
        throw new InvalidOperationException("No NuGet service indexes configured.");
}

static bool ShouldIncludePrerelease(Options opts) =>
    opts.IncludePrerelease ||
    string.Equals(Environment.GetEnvironmentVariable("BEPINEX_INCLUDE_PRERELEASE"), "true", StringComparison.OrdinalIgnoreCase);

static List<string> ResolvePackageIds(Options opts)
{
    var defaults = new[] { "HarmonyX", "BepInEx.BaseLib" };
    return ResolveDistinctValues(
        repeated: opts.BepInExPackageIds,
        csv: opts.BepInExPackageIdsCsv,
        envVar: "BEPINEX_PACKAGE_IDS",
        defaults: defaults);
}

static List<string> ResolveServiceIndexes(Options opts)
{
    var defaults = new[]
    {
        "https://api.nuget.org/v3/index.json",
        "https://nuget.bepinex.dev/v3/index.json",
    };

    return ResolveDistinctValues(
        repeated: opts.NuGetServiceIndexes,
        csv: opts.NuGetServiceIndexesCsv,
        envVar: "NUGET_SERVICE_INDEXES",
        defaults: defaults);
}

static List<string> ResolveDistinctValues(
    List<string> repeated,
    string? csv,
    string envVar,
    string[] defaults)
{
    var values = new List<string>();

    if (repeated.Count > 0)
        values.AddRange(repeated);

    if (!string.IsNullOrWhiteSpace(csv))
        values.AddRange(SplitCsv(csv));

    var envValue = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(envValue))
        values.AddRange(SplitCsv(envValue));

    if (values.Count == 0)
        values.AddRange(defaults);

    return values
        .Select(v => v.Trim())
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static IEnumerable<string> SplitCsv(string s) =>
    s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

static void CopyAllExceptListedFiles(string listPath, string sourceDir, string outDir, string label)
{
    var ignoreSet = BuildIgnoreSet(listPath);
    var (copied, skipped) = CopyFilteredFiles(sourceDir, outDir, ignoreSet);

    Console.WriteLine($"Synced {copied} files from {label}{(skipped > 0 ? $", skipped {skipped} by ignore list" : "")}");
}

static HashSet<string> BuildIgnoreSet(string listPath)
{
    var ignoreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (!File.Exists(listPath))
    {
        Console.WriteLine($"Ignore list file not found: {listPath} (copying all files).");
        return ignoreSet;
    }

    foreach (var item in ReadNonCommentLines(listPath))
    {
        var name = Path.GetFileName(item);
        ignoreSet.Add(name);
        ignoreSet.Add(Path.GetFileNameWithoutExtension(name));
    }

    Console.WriteLine($"Loaded ignore list: {Path.GetFileName(listPath)} ({ignoreSet.Count} entries)");
    return ignoreSet;
}

static (int copied, int skipped) CopyFilteredFiles(string sourceDir, string outDir, HashSet<string> ignoreSet)
{
    var copied = 0;
    var skipped = 0;

    foreach (var src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
    {
        var fileName = Path.GetFileName(src);
        var baseName = Path.GetFileNameWithoutExtension(src);

        if (ignoreSet.Contains(fileName) || ignoreSet.Contains(baseName))
        {
            skipped++;
            continue;
        }

        File.Copy(src, Path.Combine(outDir, fileName), overwrite: true);
        copied++;
    }

    return (copied, skipped);
}

static string[] ReadNonCommentLines(string listPath) =>
    File.ReadAllLines(listPath)
        .Select(l => l.Trim())
        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
        .ToArray();

static bool TryCopyBestMatchFromExtractedPackage(string extractedRoot, string fileName, string outDir)
{
    var matches = Directory.EnumerateFiles(extractedRoot, fileName, SearchOption.AllDirectories).ToList();
    if (matches.Count == 0)
        return false;

    var chosen = matches
        .OrderBy(ScoreCandidatePath)
        .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
        .First();

    File.Copy(chosen, Path.Combine(outDir, fileName), overwrite: true);
    Console.WriteLine($"Synced {fileName} from {chosen}");
    return true;
}

static int ScoreCandidatePath(string path)
{
    var norm = path.Replace('\\', '/').ToLowerInvariant();
    var score = 1000;

    if (norm.Contains("/lib/")) score -= 200;
    if (norm.Contains("/ref/")) score -= 150;
    if (norm.Contains("/runtimes/")) score -= 50;

    if (norm.Contains("/netstandard2.0")) score -= 40;
    if (norm.Contains("/net48")) score -= 40;
    if (norm.Contains("/net462")) score -= 30;
    if (norm.Contains("/net46")) score -= 20;
    if (norm.Contains("/net452")) score -= 10;

    score += norm.Count(c => c == '/');
    return score;
}

static async Task<(string serviceIndex, string packageBaseAddress, string version)> ResolveLatestFromAnySourceAsync(
    List<string> serviceIndexes,
    string packageId,
    bool includePrerelease,
    Dictionary<string, string> packageBaseCache,
    CancellationToken ct = default)
{
    foreach (var serviceIndex in serviceIndexes)
    {
        var baseAddress = await GetPackageBaseAddressCachedAsync(serviceIndex, packageBaseCache, ct);
        var version = await TryGetLatestNuGetVersionOnlineAsync(baseAddress, packageId, includePrerelease, ct);

        if (version is not null)
            return (serviceIndex, baseAddress, version);
    }

    throw new InvalidOperationException($"Package '{packageId}' not found in any configured NuGet sources.");
}

static async Task<string> GetPackageBaseAddressCachedAsync(
    string serviceIndexUrl,
    Dictionary<string, string> cache,
    CancellationToken ct)
{
    if (cache.TryGetValue(serviceIndexUrl, out var cached))
        return cached;

    var resolved = await GetNuGetPackageBaseAddressAsync(serviceIndexUrl, ct);
    cache[serviceIndexUrl] = resolved;
    return resolved;
}

static async Task<string> GetNuGetPackageBaseAddressAsync(string serviceIndexUrl, CancellationToken ct = default)
{
    using var doc = await FetchJsonDocumentAsync(serviceIndexUrl, TimeSpan.FromSeconds(30), ct);

    foreach (var resource in doc.RootElement.GetProperty("resources").EnumerateArray())
    {
        if (!resource.TryGetProperty("@type", out var typeProp)) continue;
        if (!resource.TryGetProperty("@id", out var idProp)) continue;

        var type = typeProp.GetString();
        var id = idProp.GetString();

        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
            continue;

        if (type.StartsWith("PackageBaseAddress/", StringComparison.OrdinalIgnoreCase))
            return EnsureTrailingSlash(id.Trim());
    }

    throw new InvalidOperationException($"Service index does not contain PackageBaseAddress: {serviceIndexUrl}");
}

static string EnsureTrailingSlash(string url) =>
    url.EndsWith('/') ? url : url + "/";

static async Task<string?> TryGetLatestNuGetVersionOnlineAsync(
    string packageBaseAddress,
    string packageId,
    bool includePrerelease,
    CancellationToken ct = default)
{
    var idLower = packageId.ToLowerInvariant();
    var indexUrl = $"{packageBaseAddress.TrimEnd('/')}/{idLower}/index.json";

    using var http = CreateHttpClient(TimeSpan.FromSeconds(30));
    using var resp = await http.GetAsync(indexUrl, ct);

    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        return null;

    resp.EnsureSuccessStatusCode();

    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

    var versions = ExtractVersions(doc, includePrerelease);
    if (versions.Count == 0)
        return null;

    versions.Sort(CompareNuGetLikeVersions);
    return versions.Last();
}

static List<string> ExtractVersions(JsonDocument doc, bool includePrerelease)
{
    var versions = doc.RootElement
        .GetProperty("versions")
        .EnumerateArray()
        .Select(v => v.GetString())
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!)
        .ToList();

    if (!includePrerelease)
        versions = versions.Where(v => !v.Contains('-', StringComparison.Ordinal)).ToList();

    return versions;
}

static async Task<string> DownloadAndExtractNuGetPackageAsync(
    string packageBaseAddress,
    string packageId,
    string version,
    CancellationToken ct = default)
{
    var idLower = packageId.ToLowerInvariant();
    var vLower = version.ToLowerInvariant();

    var nupkgUrl = BuildNupkgUrl(packageBaseAddress, idLower, vLower);
    var workDir = CreateUniqueWorkDirectory(idLower, vLower);
    var extractDir = Path.Combine(workDir, "extracted");
    Directory.CreateDirectory(extractDir);

    var nupkgPath = Path.Combine(workDir, $"{idLower}.{vLower}.nupkg");
    await DownloadFileAsync(nupkgUrl, nupkgPath, ct);

    ZipFile.ExtractToDirectory(nupkgPath, extractDir, overwriteFiles: true);
    return extractDir;
}

static string BuildNupkgUrl(string packageBaseAddress, string idLower, string vLower)
{
    var baseUrl = EnsureTrailingSlash(packageBaseAddress);
    return $"{baseUrl}{idLower}/{vLower}/{idLower}.{vLower}.nupkg";
}

static string CreateUniqueWorkDirectory(string idLower, string vLower)
{
    var dir = Path.Combine(
        Path.GetTempPath(),
        "sync-managed",
        "nuget",
        idLower,
        vLower,
        Guid.NewGuid().ToString("n"));

    Directory.CreateDirectory(dir);
    return dir;
}

static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
{
    using var http = CreateHttpClient(TimeSpan.FromSeconds(120));
    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
    resp.EnsureSuccessStatusCode();

    await using var inStream = await resp.Content.ReadAsStreamAsync(ct);
    await using var outStream = File.Create(destinationPath);
    await inStream.CopyToAsync(outStream, ct);
}

static HttpClient CreateHttpClient(TimeSpan timeout)
{
    var http = new HttpClient { Timeout = timeout };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("sync-managed/1.0");
    return http;
}

static async Task<JsonDocument> FetchJsonDocumentAsync(string url, TimeSpan timeout, CancellationToken ct)
{
    using var http = CreateHttpClient(timeout);
    using var resp = await http.GetAsync(url, ct);
    resp.EnsureSuccessStatusCode();

    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
}

static int CompareNuGetLikeVersions(string? a, string? b)
{
    if (a is null && b is null) return 0;
    if (a is null) return -1;
    if (b is null) return 1;

    var (va, prea) = ParseVersionString(a);
    var (vb, preb) = ParseVersionString(b);

    var c = va.CompareTo(vb);
    if (c != 0) return c;

    return ComparePrereleaseLabels(prea, preb);
}

static (Version version, string? prerelease) ParseVersionString(string versionString)
{
    var parts = versionString.Split('-', 2, StringSplitOptions.TrimEntries);
    var verPart = parts[0];
    var prePart = parts.Length > 1 ? parts[1] : null;

    if (!Version.TryParse(verPart, out var v))
        v = new Version(0, 0, 0, 0);

    return (v, prePart);
}

static int ComparePrereleaseLabels(string? a, string? b)
{
    var aStable = string.IsNullOrEmpty(a);
    var bStable = string.IsNullOrEmpty(b);

    if (aStable && !bStable) return 1;
    if (!aStable && bStable) return -1;

    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
}

static Options ParseArgs(string[] args)
{
    var o = new Options();

    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        string Next() => (i + 1 < args.Length) ? args[++i] : throw new ArgumentException($"Missing value after {a}");

        switch (a)
        {
            case "--managedPath" or "-m":
                o.ManagedPath = Next();
                break;

            case "--steamPath" or "-s":
                o.SteamPath = Next();
                break;

            case "--listPath" or "-l" or "--ignorePath" or "-i":
                o.ListPath = Next();
                break;

            case "--bepInExPackageId":
                o.BepInExPackageIds.Add(Next());
                break;

            case "--bepInExPackageIds":
                o.BepInExPackageIdsCsv = Next();
                break;

            case "--includePrerelease":
                o.IncludePrerelease = true;
                break;

            case "--nugetServiceIndex":
                o.NuGetServiceIndexes.Add(Next());
                break;

            case "--nugetServiceIndexes":
                o.NuGetServiceIndexesCsv = Next();
                break;

            case "--outDir" or "-o":
                o.OutDir = Next();
                break;

            case "--help" or "-h":
                PrintHelp();
                Environment.Exit(0);
                break;

            default:
                throw new ArgumentException($"Unknown argument: {a}");
        }
    }

    return o;
}

static void PrintHelp()
{
    Console.WriteLine("""
Usage:
  dotnet run tools/sync-managed.cs -- [options]

Options:
  -m|--managedPath <path>           Explicit path to .../Valheim/valheim_Data/Managed
  -s|--steamPath <path>             Explicit Steam install folder (optional)
  -i|--ignorePath <path>            Ignore list file (default: ./ignore-managed.md). Deprecated alias: -l|--listPath

  --bepInExPackageId <id>           Optional (repeatable): override package ids (e.g. --bepInExPackageId HarmonyX --bepInExPackageId BepInEx.BaseLib)
  --bepInExPackageIds <csv>         Optional: override package ids as CSV

  --nugetServiceIndex <url>         Optional (repeatable): add/override v3 service index sources
  --nugetServiceIndexes <csv>       Optional: add/override v3 service index sources as CSV

  --includePrerelease               If set, allows prerelease versions when picking latest online
  -o|--outDir <path>                Output dir (default: ./lib/net46)

Environment:
  VALHEIM_MANAGED
  STEAM_PATH / STEAM_DIR
  BEPINEX_PACKAGE_IDS               CSV list of package ids
  BEPINEX_INCLUDE_PRERELEASE        true/false
  NUGET_SERVICE_INDEXES             CSV list of v3 index.json sources

Ignore list format (ignore-managed.md):
  # Lines starting with '#' are comments
  # List file names (with or without extension), one per line
  # Examples:
  mscorlib.dll
  System
""");
}

static string FindValheimManagedPath(string? steamPathOverride)
{
    var steamInstall =
        FirstNonEmpty(
            steamPathOverride,
            Environment.GetEnvironmentVariable("STEAM_PATH"),
            Environment.GetEnvironmentVariable("STEAM_DIR"))
        ?? GetSteamInstallPathAuto();

    if (string.IsNullOrWhiteSpace(steamInstall) || !Directory.Exists(steamInstall))
        throw new InvalidOperationException("Steam install path not found. Use --steamPath or set STEAM_PATH/STEAM_DIR/VALHEIM_MANAGED.");

    var libraries = GetSteamLibraryRoots(steamInstall);
    var valheimRoot = ResolveSteamAppInstallDir(libraries, appId: "892970");
    var dataDir = FindChildDirIgnoreCase(valheimRoot, "valheim_data");
    var managedDir = FindChildDirIgnoreCase(dataDir, "managed");
    return managedDir;
}

static string FindChildDirIgnoreCase(string parent, string childName)
{
    var match = Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly)
        .FirstOrDefault(d => string.Equals(Path.GetFileName(d), childName, StringComparison.OrdinalIgnoreCase));
        return match ?? Path.Combine(parent, childName);
 }

static string? GetSteamInstallPathAuto()
{
    if (OperatingSystem.IsWindows())
        return GetSteamInstallPathFromRegistry();

    if (OperatingSystem.IsLinux())
        return GetSteamInstallPathFromLinux();

    return null;
}

static string? GetSteamInstallPathFromRegistry()
{
    var candidates = new[]
    {
        @"SOFTWARE\WOW6432Node\Valve\Steam",
        @"SOFTWARE\Valve\Steam"
    };

    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

        foreach (var subKeyPath in candidates)
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            var installPath = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
                return installPath;
        }
    }

    return null;
}

static string? GetSteamInstallPathFromLinux()
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    var candidates = new[]
    {
        Path.Combine(home, ".local", "share", "Steam"),
        Path.Combine(home, ".steam", "steam"),
        Path.Combine(home, ".steam", "root"),
        Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
    };

    foreach (var c in candidates)
        if (Directory.Exists(Path.Combine(c, "steamapps")))
            return c;

    return null;
}

static List<string> GetSteamLibraryRoots(string steamInstallPath)
{
    var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamInstallPath };

    var vdfPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
    if (!File.Exists(vdfPath))
        return libs.ToList();

    var pathRegex = new Regex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    foreach (var line in File.ReadLines(vdfPath))
    {
        var match = pathRegex.Match(line);
        if (!match.Success) continue;

        var normalized = NormalizeSteamPathFromVdf(match.Groups[1].Value.Trim());
        if (Directory.Exists(normalized))
            libs.Add(normalized);
    }

    return libs.ToList();
}

static string NormalizeSteamPathFromVdf(string raw)
{
    var normalized = raw.Replace(@"\\", @"\");
    if (OperatingSystem.IsLinux())
        normalized = normalized.Replace('\\', '/');
    return normalized;
}

static string ResolveSteamAppInstallDir(List<string> libraries, string appId)
{
    foreach (var libRoot in libraries)
    {
        var steamApps = Path.Combine(libRoot, "steamapps");
        if (!Directory.Exists(steamApps))
            continue;

        var manifest = Path.Combine(steamApps, $"appmanifest_{appId}.acf");
        var common = Path.Combine(steamApps, "common");

        var installDirName = TryReadInstallDirFromManifest(manifest);

        if (!string.IsNullOrWhiteSpace(installDirName))
        {
            var candidate = Path.Combine(common, installDirName);
            if (Directory.Exists(candidate))
                return candidate;
        }

        var fallback = Path.Combine(common, "Valheim");
        if (Directory.Exists(fallback))
            return fallback;
    }

    throw new DirectoryNotFoundException($"Could not locate Steam app install dir for appId={appId}.");
}

static string? TryReadInstallDirFromManifest(string manifestPath)
{
    if (!File.Exists(manifestPath))
        return null;

    var installDirRegex = new Regex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    foreach (var line in File.ReadLines(manifestPath))
    {
        var match = installDirRegex.Match(line);
        if (match.Success)
            return match.Groups[1].Value.Trim();
    }

    return null;
}

static void CleanDestinationFolder(string outDir, string repoRoot)
{
    var fullOutDir = Path.GetFullPath(outDir);
    var fullRepoRoot = Path.GetFullPath(repoRoot);
    var pathRoot = Path.GetPathRoot(fullOutDir) ?? "";

    ValidateCleanTarget(fullOutDir, fullRepoRoot, pathRoot);

    if (!Directory.Exists(fullOutDir))
        return;

    foreach (var dir in Directory.EnumerateDirectories(fullOutDir, "*", SearchOption.TopDirectoryOnly))
        Directory.Delete(dir, recursive: true);

    foreach (var file in Directory.EnumerateFiles(fullOutDir, "*", SearchOption.TopDirectoryOnly))
        File.Delete(file);

    Console.WriteLine($"Cleaned destination folder: {fullOutDir}");
}

static void ValidateCleanTarget(string fullOutDir, string fullRepoRoot, string pathRoot)
{
    if (PathsEqual(fullOutDir, fullRepoRoot))
        throw new InvalidOperationException($"Refusing to clean destination because it equals repo root: {fullOutDir}");

    if (PathsEqual(NormalizeDir(fullOutDir), NormalizeDir(pathRoot)))
        throw new InvalidOperationException($"Refusing to clean destination because it looks like a filesystem root: {fullOutDir}");
}

static string NormalizeDir(string p) =>
    p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

static bool PathsEqual(string a, string b) =>
    string.Equals(NormalizeDir(a), NormalizeDir(b), StringComparison.OrdinalIgnoreCase);

static string NormalizePathCasingIfNeeded(string path)
{
    var full = Path.GetFullPath(path);

    if (Directory.Exists(full) || File.Exists(full))
        return full;

    if (OperatingSystem.IsWindows())
        return full;

    return TryResolveExistingPathIgnoreCase(full) ?? full;
}

static string? TryResolveExistingPathIgnoreCase(string fullPath)
{
    var root = Path.GetPathRoot(fullPath);
    if (string.IsNullOrEmpty(root))
        return null;

    var current = root;
    var rest = fullPath.Substring(root.Length);

    var parts = rest.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

    foreach (var part in parts)
    {
        if (!Directory.Exists(current))
            return null;

        var match = Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), part, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return null;

        current = match;
    }

    return current;
}

sealed class Options
{
    public string? ManagedPath { get; set; }
    public string? SteamPath { get; set; }
    public string? ListPath { get; set; }
    public string? OutDir { get; set; }
    public List<string> BepInExPackageIds { get; } = new();
    public string? BepInExPackageIdsCsv { get; set; }
    public List<string> NuGetServiceIndexes { get; } = new();
    public string? NuGetServiceIndexesCsv { get; set; }
    public bool IncludePrerelease { get; set; }
}
