using Gardener.Core;
using Gardener.Core.MSBuild;
using Gardener.Core.Packaging;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UpgradeRepo.Cpm
{
    internal class CpmUpgradePlugin : IUpgradePlugin
    {
        internal const string DirectoryPackagesProps = "Directory.Packages.props";
        internal const string DirectoryBuildProps = "Directory.Build.props";
        private readonly ConcurrentDictionary<string, ConcurrentBag<PackageReferenceLocation>> _packageMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ConcurrentBag<Package>> _supplementalPackages = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentBag<ProjectFile> _files = new();
        private readonly ILogger _logger;
        private readonly IPackageVersionRepository? _packageVersionRepository;
        private IFileSystem? _fileSystem;
        private List<AzureArtifactsFeed>? _artifactsFeedsCache;

        public CpmUpgradePlugin(ILogger logger, IPackageVersionRepository? packageVersionRepository)
        {
            _logger = logger;
            _packageVersionRepository = packageVersionRepository;
        }

        public async Task<bool> CanApplyAsync(OperateContext context)
        {
            _fileSystem = context.FileSystem;
            string dpPropsContent = await _fileSystem.FileExistsAsync(DirectoryBuildProps)
                ? (await MSBuildFile.ReadAsync(_fileSystem, DirectoryBuildProps)).Content
                : string.Empty;

            // If the Directory.Build.props mentions the legacy version or they have a Package.props
            // they can't be onboarded.
            if (Regex.IsMatch(dpPropsContent, @"Name=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(dpPropsContent, @"SDK=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                await _fileSystem.FileExistsAsync("Pacakges.props"))
            {
                return false;
            }

            // Already enabled, no action needed
            return !await _fileSystem.FileExistsAsync(DirectoryPackagesProps);
        }

        public async Task<bool> ApplyAsync(OperateContext context)
        {
            _fileSystem = context.FileSystem;
            var files = await _fileSystem.EnumerateFiles(new[] { "*.??proj", "*.???proj", "*.proj", "*.targets", "*.props" });

            if (!files.Any())
            {
                _logger.LogInformation("No PackageReferences detected.");
                return false;
            }

            foreach (var file in files)
            {
                AddFile(file);
            }

            await ReadAllPackagesAsync();

            if (_packageMap.IsEmpty)
            {
                _logger.LogInformation("No PackageReferences detected.");
                return false;
            }

            var wroteFiles = await WriteAllUpdatesAsync();

            if (!wroteFiles)
            {
                _logger.LogInformation("No PackageReferences detected.");
                return false;
            }

            ShowConflicts();

            var lineEndingDirectoryBuildProps = await UpdateDirectoryBuildProps(_fileSystem, DirectoryBuildProps);

            var packagePropsContent = await GeneratePackageProps(lineEndingDirectoryBuildProps);

            await _fileSystem.WriteAllTextAsync(DirectoryPackagesProps, packagePropsContent);

            return true;
        }

        /// <summary>
        /// Update D.B.props file to enable CPM feature.
        /// </summary>
        /// <param name="fileSystem">File System</param>
        /// <param name="dbPropsPath">Path to the D.B.props file.</param>
        /// <returns>Line endings detected or used to generate the file.</returns>
        private async Task<string> UpdateDirectoryBuildProps(IFileSystem fileSystem, string dbPropsPath)
        {
            var lineEnding = Environment.NewLine;

            const string defaultDbPropsContent = """
                                                 <Project>
                                                   <PropertyGroup>
                                                     <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                                                   </PropertyGroup>
                                                 </Project>

                                                 """;
            _logger.LogInformation("Setting EnableCentralPackageVersions Property");
            if (await fileSystem.FileExistsAsync(dbPropsPath))
            {
                var dbPropsFile = await MSBuildFile.ReadAsync(fileSystem, dbPropsPath);
                lineEnding = dbPropsPath.DetermineLineEnding();

                dbPropsFile.SetProperties(new[] { new MSBuildFile.Property("ManagePackageVersionsCentrally", "true") });
                await fileSystem.WriteAllTextAsync(dbPropsPath, dbPropsFile.Content);
            }
            else
            {
                await fileSystem.WriteAllTextAsync(dbPropsPath, defaultDbPropsContent);
            }

            return lineEnding;
        }

        public IEnumerable<Package> GetPackages()
        {
            foreach (var package in _packageMap)
            {
                foreach (var p2 in package.Value)
                {
                    yield return p2.Package;
                }
            }
        }

        public void AddFile(string file)
        {
            if ((file.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".props", StringComparison.OrdinalIgnoreCase)) &&
                !file.EndsWith(".vdproj", StringComparison.OrdinalIgnoreCase))
            {
                _files.Add(new ProjectFile(_fileSystem!, _logger, file));
            }
        }

        public async Task ReadAllPackagesAsync()
        {
            foreach (ProjectFile file in _files)
            {
                await ReadFileAsync(file);
            }
        }

        public async Task<bool> WriteAllUpdatesAsync()
        {
            var wroteFiles = false;
            foreach (ProjectFile file in _files)
            {
                wroteFiles = await file.WritePackagesAsync(ResolveVersion) || wroteFiles;
            }

            return wroteFiles;
        }

        public void ShowConflicts()
        {
            foreach (var package in _packageMap)
            {
                var msBuildPropertiesCount = package.Value.Count(p => p.Package.VersionType == PackageVersionType.MSBuildProperty);
                var wildCardsCount = package.Value.Count(p => p.Package.VersionType == PackageVersionType.Wildcard);
                var floatingCount = package.Value.Count(p => p.Package.VersionType == PackageVersionType.VersionRange);

                // Log a warning if more than one of the categories is populated.
                if (new[] { msBuildPropertiesCount, wildCardsCount, floatingCount }.Count(c => c > 0) > 1)
                {
                    _logger.LogWarning($"Repo has multiple types of package specifications. MSBuild: {msBuildPropertiesCount}, Wildcard: {wildCardsCount}, Range: {floatingCount}");
                }

                if (package.Value.Count > 1)
                {
                    _logger.LogInformation($"{package.Key} : {string.Join(" ", package.Value)}");
                }
            }
        }

        private async Task ReadFileAsync(ProjectFile file)
        {
            await file.ReadPackagesAsync();
            var packages = file.GetPackages();
            var supplemental = file.GetSupplementalPackages();

            foreach (var package in packages)
            {
                ConcurrentBag<PackageReferenceLocation> locations = _packageMap.GetOrAdd(
                    package.Name,
                    _ => new ConcurrentBag<PackageReferenceLocation>());

                locations.Add(new PackageReferenceLocation(package, file));
            }

            foreach (var package in supplemental)
            {
                ConcurrentBag<Package> locations = _supplementalPackages.GetOrAdd(
                    package.Name,
                    _ => new ConcurrentBag<Package>());

                locations.Add(package);
            }
        }

        public async Task<string> GeneratePackageProps(string lineEnding)
        {
            const string EmptyProjectTemplate =
                """
                <Project>
                  <ItemGroup>
                {0}  </ItemGroup>
                </Project>

                """;

            const string PackageVersionTemplate = @"    <PackageVersion Include=""{0}"" Version=""{1}"" />{2}";

            StringBuilder sb = new();
            var packages = _packageMap
                .Select(kvp => kvp.Value.Max(pkg => pkg.Package)!)
                .OrderBy(pkg => pkg.Name);

            foreach (var package in packages)
            {
                var version = await ResolveSpecialVersionCases(package);
                sb.Append(string.Format(PackageVersionTemplate, package.Name, version, lineEnding));
            }

            return string.Format(EmptyProjectTemplate, sb);
        }

        private async Task<string> ResolveSpecialVersionCases(Package package)
        {
            // If the only version we found for the package was "Unknown", it won't be valid here.
            // In this case the repo probably specified it as an Update after the declaration.
            // NOTE: This is somewhat risky as we don't know don't have the import graph. These could
            // come from anywhere in the repo.
            if (package.VersionType == PackageVersionType.Unknown)
            {
                package = ResolveUnknownPackage(package);
            }

            // We can't use a * version in D.P.props! Get the latest version.
            if (package.VersionType == PackageVersionType.Wildcard)
            {
                package = await ResolveWildcardPackage(package);
            }

            if (package.VersionType == PackageVersionType.VersionRange && package.VersionString.Contains('*'))
            {
                // I guess this could be handled if we find a lot of these.
                throw new InvalidOperationException($"Version Ranges with wildcards not allowed in Directory.Build.props! Could not resolve {package.Name} {package.VersionString}");
            }

            return package.VersionString;
        }

        private async Task<Package> ResolveWildcardPackage(Package package)
        {
            if (_packageVersionRepository != null && VersionRange.TryParse(package.VersionString, out VersionRange? versionRange))
            {
                foreach (var feed in await GetFeeds())
                {
                    NuGetVersion? bestMatch = await _packageVersionRepository.FindBestMatchPackageVersionAsync(feed, package.Name, versionRange, CancellationToken.None);
                    var matchVersion = bestMatch?.ToString();
                    if (!string.IsNullOrEmpty(matchVersion))
                    {
                        _logger.LogInformation($"Resolved package '{package.Name} from {package.VersionString} to {matchVersion}.");
                        return new Package(package.Name, matchVersion);
                    }
                }
            }

            // Checking the feed failed, the result will be an invalid Directory.Packages.Props.
            throw new InvalidOperationException($"* Version not allowed in Directory.Packages.Props! Could not resolve {package.Name} {package.VersionString}");
        }

        private Package ResolveUnknownPackage(Package package)
        {
            if (!_supplementalPackages.ContainsKey(package.Name))
            {
                throw new InvalidOperationException($"Unknown version specified: {package.Name} {package.VersionString}");
            }

            var supplemental = _supplementalPackages[package.Name].Max();
            if (supplemental!.VersionType != PackageVersionType.Unknown)
            {
                // Replace with this version. It might still have a wildcard, so resolve it below
                package = supplemental;
            }

            return package;
        }

        private async Task<List<AzureArtifactsFeed>> GetFeeds()
        {
            if (_artifactsFeedsCache == null)
            {
                _artifactsFeedsCache = new List<AzureArtifactsFeed>();
                var nugetConfigs = await _fileSystem!.EnumerateFiles(new[] { "*nuget.config" });

                if (nugetConfigs == null || !nugetConfigs.Any())
                {
                    throw new InvalidOperationException("* Version not allowed in Directory.Build.props! - No nuget.config");
                }

                // OrderBy so the shortest (root) will always be checked first.
                foreach (var config in nugetConfigs.OrderBy(c => c.Length))
                {
                    string configFile = await _fileSystem.ReadAllTextAsync(config);
                    _artifactsFeedsCache.AddRange(AzureArtifactsFeed.ParseNuGetConfig(configFile));
                }

                if (_artifactsFeedsCache.Count < 1)
                {
                    throw new InvalidOperationException(
                        $"* Version not allowed in Directory.Build.props! - Feed count: {_artifactsFeedsCache.Count}");
                }
            }

            return _artifactsFeedsCache;
        }

        /// <summary>
        /// When resolving a PackageReference, ideally there is no Version specified. When we have a conflict,
        /// We will choose the lower versions in leaf projects. This is a valid scenario when, for example, you
        /// want to compile against a plug-in for API compatibility.
        /// </summary>
        /// <param name="package">The package name/version in a Project to resolve</param>
        /// <returns>Version string to use in the xml file (empty when no conflicts)</returns>
        private string ResolveVersion(Package package)
        {
            var locations = _packageMap[package.Name];

            var maxVersion = locations.Max(location => location.Package);
            return package.Equals(maxVersion) ? string.Empty : package.VersionString;
        }
    }
}
