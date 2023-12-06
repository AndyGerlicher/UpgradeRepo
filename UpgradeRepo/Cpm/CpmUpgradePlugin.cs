using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Gardener.Core;
using Gardener.Core.MSBuild;

namespace UpgradeRepo.Cpm
{
    internal class CpmUpgradePlugin : IUpgradePlugin
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<PackageReferenceLocation>> _packageMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentBag<ProjectFile> _files = new();

        public async Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            var dbPropsPath = Path.Combine(options.Path, "Directory.Build.props");
            string dpPropsContent = await fileSystem.FileExistsAsync(dbPropsPath)
                ? (await MSBuildFile.ReadAsync(fileSystem, dbPropsPath)).Content
                : string.Empty;

            // If the Directory.Build.props mentions the legacy version or they have a Package.props
            // they can't be onboarded.

            if (Regex.IsMatch(dpPropsContent, @"Name=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(dpPropsContent, @"SDK=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                await fileSystem.FileExistsAsync("Pacakges.props"))
            {
                return false;
            }

            return true;
        }

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            var dbPropsPath = Path.Combine(options.Path, "Directory.Build.props");
            var packagePropsFile = Path.Combine(options.Path, "Directory.Packages.props");
            var files = Directory.GetFiles(options.Path, "*.csproj", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                AddFile(file);
            }

            ReadAllPackages();
            WriteAllUpdates();
            ShowConflicts();

            await EnableFeature(fileSystem, dbPropsPath);

            var packagePropsContent = GeneratePackageProps();
            await fileSystem.WriteAllTextAsync(packagePropsFile, packagePropsContent);

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
            _files.Add(new ProjectFile(file));
        }

        public void ReadAllPackages()
        {
            Parallel.ForEach(_files,
                new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                ReadFile);
        }

        public void WriteAllUpdates()
        {
            Parallel.ForEach(_files,
                new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                UpdateFile);
        }

        public void ShowConflicts()
        {
            foreach (var package in _packageMap)
            {
                if (package.Value.Count > 1)
                {
                    Console.WriteLine($"{package.Key} : {string.Join(" ", package.Value)}");
                }
            }
        }

        private void ReadFile(ProjectFile file)
        {
            file.ReadPackages();
            var packages = file.GetPackages();

            foreach (var package in packages)
            {
                _packageMap.AddOrUpdate(package.Name,
                    (_) => new ConcurrentBag<PackageReferenceLocation>() { new(package, file) },
                    (_, bag) =>
                    {
                        bag.Add(new PackageReferenceLocation(package, file));
                        return bag;
                    });
            }
        }

        private void UpdateFile(ProjectFile file)
        {
            file.WritePackages(VersionResolver);
        }

        public string GeneratePackageProps()
        {
            var packages = from package in _packageMap
                orderby package.Key
                select new Package(package.Key, package.Value.Min(p => p.Package.Version));

            return ProjectFileHelpers.GeneratePackageProps(packages);
        }

        private string VersionResolver(Package package)
        {
            var locations = _packageMap[package.Name];

            var minVersion = locations.Select((location => location.Package.Version)).Min(v => v);
            return package.Version == minVersion ? string.Empty : package.Version.ToString();

        }
    }
}
