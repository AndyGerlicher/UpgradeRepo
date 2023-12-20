using Gardener.Core;
using Gardener.Core.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace UpgradeRepo.Cpm
{
    internal class CpmUpgradePlugin : IUpgradePlugin
    {
        private const string DirectoryPackagesProps = "Directory.Packages.props";
        private const string DirectoryBuildProps = "Directory.Build.props";
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
            string dpPropsContent = await fileSystem.FileExistsAsync(DirectoryBuildProps)
                ? (await MSBuildFile.ReadAsync(fileSystem, DirectoryBuildProps)).Content
                : string.Empty;

            // If the Directory.Build.props mentions the legacy version or they have a Package.props
            // they can't be onboarded.
            if (Regex.IsMatch(dpPropsContent, @"Name=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(dpPropsContent, @"SDK=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                await fileSystem.FileExistsAsync("Pacakges.props"))
            {
                return false;
            }
            
            // Already enabled, no action needed
            if (await fileSystem.FileExistsAsync(DirectoryPackagesProps))
            {
                return false;
            }

            return true;
        }

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;

            var files = await _fileSystem.EnumerateFiles(new[] { "*.*proj", "*.targets", "*.props" });

            foreach (var file in files)
            {
                AddFile(file);
            }

            await ReadAllPackagesAsync();
            await WriteAllUpdatesAsync();
            ShowConflicts();

            await EnableFeature(fileSystem, DirectoryBuildProps);

            var packagePropsContent = GeneratePackageProps();
            await fileSystem.WriteAllTextAsync(DirectoryPackagesProps, packagePropsContent);

            return true;
        }

        private async Task EnableFeature(IFileSystem fileSystem, string dbPropsPath)
        {
            const string defaultDbPropsContent = @"<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
";
            Console.WriteLine(" * Setting EnableCentralPackageVersions Property");
            if (await fileSystem.FileExistsAsync(dbPropsPath))
            {
                var dbPropsFile = await MSBuildFile.ReadAsync(fileSystem, dbPropsPath);
                dbPropsFile.SetProperties(new[] { new MSBuildFile.Property("ManagePackageVersionsCentrally", "true") });
                await fileSystem.WriteAllTextAsync(dbPropsPath, dbPropsFile.Content);
            }
            else
            {
                await fileSystem.WriteAllTextAsync(dbPropsPath, defaultDbPropsContent);
            }
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
            _files.Add(new ProjectFile(_fileSystem, file));
        }

        public async Task ReadAllPackagesAsync()
        {
            await Parallel.ForEachAsync(
                _files,
                new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                async (file, cancellationToken) =>
                {
                    await ReadFileAsync(file);
                });
        }

        public async Task WriteAllUpdatesAsync()
        {
            await Parallel.ForEachAsync(
                _files,
                new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                async (file, cancellationToken) =>
                {
                    await UpdateFileAsync(file);
                });
        }

        public void ShowConflicts()
        {
            foreach (var package in _packageMap)
            {
                var msBuildPropeties = package.Value.Where(p => p.Package.VersionType == PackageVersionType.MSBuildProperty).Distinct().ToList();
                var wildCards = package.Value.Where(p => p.Package.VersionType == PackageVersionType.Wildcard).ToList();

                if (msBuildPropeties.Count > 0 && wildCards.Count > 0)
                {
                    throw new InvalidOperationException("You can't have both MSBuild properties and Wildcards!");
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
                _packageMap.AddOrUpdate(
                    package.Name,
                    (_) => new ConcurrentBag<PackageReferenceLocation>() { new(package, file) },
                    (_, bag) =>
                    {
                        bag.Add(new PackageReferenceLocation(package, file));
                        return bag;
                    });
            }
        }

        private async Task UpdateFileAsync(ProjectFile file)
        {
            await file.WritePackagesAsync(VersionResolver);
        }

        public string GeneratePackageProps()
        {
            var packages =
                from kvp in _packageMap
                let maxPackage = (from pl in kvp.Value
                    orderby pl.Package descending
                    select pl.Package).FirstOrDefault()
                where maxPackage != null
                select maxPackage;



            return ProjectFileHelpers.GeneratePackageProps(packages);
        }

        /// <summary>
        /// When resolving a PackageReference, ideally there is no Version specified. When we have a conflict,
        /// We will choose the lower versions in leaf projects. This is a valid scenario when, for example, you
        /// want to compile against a plug-in for API compatibility.
        /// </summary>
        /// <param name="package">The package name/version in a Project to resolve</param>
        /// <returns>Version string to use in the xml file (empty when no conflicts)</returns>
        private string VersionResolver(Package package)
        {
            var locations = _packageMap[package.Name];

            var maxVersion = locations.Select(location => location.Package).Max(v => v);
            return package.Equals(maxVersion) ? string.Empty : package.VersionString;
        }
    }
}
