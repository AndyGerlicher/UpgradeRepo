using Gardener.Core;
using Gardener.Core.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace UpgradeRepo.Cpm
{
    internal class CpmUpgradePlugin : IUpgradePlugin
    {
        internal const string DirectoryPackagesProps = "Directory.Packages.props";
        internal const string DirectoryBuildProps = "Directory.Build.props";
        private readonly ConcurrentDictionary<string, ConcurrentBag<PackageReferenceLocation>> _packageMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentBag<ProjectFile> _files = new();
        private IFileSystem _fileSystem;
        private ILogger _logger;

        public CpmUpgradePlugin(ILogger logger) : this(new FileSystem(), logger)
        { }

        public CpmUpgradePlugin(IFileSystem fileSystem, ILogger logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public async Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
			_fileSystem = fileSystem;
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

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
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

            await WriteAllUpdatesAsync();
            ShowConflicts();

            var lineEndingDirectoryBuildProps = await UpdateDirectoryBuildProps(_fileSystem, DirectoryBuildProps);

            var packagePropsContent = GeneratePackageProps(lineEndingDirectoryBuildProps);

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
                _files.Add(new ProjectFile(_fileSystem, _logger, file));
            }
        }

        public async Task ReadAllPackagesAsync()
        {
            foreach (ProjectFile file in _files)
            {
                await ReadFileAsync(file);
            }
        }

        public async Task WriteAllUpdatesAsync()
        {
            foreach (ProjectFile file in _files)
            {
                await UpdateFileAsync(file);
            }
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

            foreach (var package in packages)
            {
                ConcurrentBag<PackageReferenceLocation> locations = _packageMap.GetOrAdd(
                    package.Name,
                    _ => new ConcurrentBag<PackageReferenceLocation>());

                locations.Add(new PackageReferenceLocation(package, file));
            }
        }

        private async Task UpdateFileAsync(ProjectFile file)
        {
            await file.WritePackagesAsync(ResolveVersion);
        }

        public string GeneratePackageProps(string lineEnding)
        {
            var packages = _packageMap.Select(kvp => kvp.Value.Max(pkg => pkg.Package)!);

            return ProjectFileHelpers.GeneratePackageProps(packages, lineEnding);
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
