using System.Text;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace UpgradeRepo.Cpm
{
    internal static class ProjectFileHelpers
    {
        public static Package GetPackageFromLine(string line)
        {
            var pattern = @"<PackageReference Include=""(?<name>[^""]*)"".*Version=""(?<version>[^""]*)""";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var match = regex.Match(line);

            var name = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;

            return new Package(name, string.IsNullOrEmpty(version) ? null : new NuGetVersion(version));
        }

        public static bool LineContainsPackageReference(this string line)
        {
            return Regex.IsMatch(line, @"<PackageReference Include=""(?<name>[^""]*)"".*Version=""(?<version>[^""]*)""");
        }

        /// <summary>
        /// Update the version on a single PackageReference line
        /// </summary>
        /// <param name="line">Line specifying the PackageReference </param>
        /// <param name="versionResolver">Method to resolve version conflicts</param>
        /// <returns>Update PR line</returns>
        public static string UpdateVersion(string line, Func<Package, string> versionResolver)
        {
            // <PackageReference Include="Package" Version="X"
            // Removes version or sets VersionOverride if higher than the central version
            var package = ProjectFileHelpers.GetPackageFromLine(line);
            var newVersion = versionResolver(package);
            newVersion = string.IsNullOrEmpty(newVersion) ?
                string.Empty :
                $@" VersionOverride=""{newVersion}""";

            var pattern = @" Version="".*""";
            return Regex.Replace(line, pattern, newVersion, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Write out a Directory.Packages.props file
        /// </summary>
        /// <param name="packages">Set of packages/versions</param>
        /// <returns>MSBuild file XML</returns>
        public static string GeneratePackageProps(IEnumerable<Package> packages)
        {
            const string emptyProjectTemplate = @"<Project>
  <ItemGroup>
{0}  </ItemGroup>
</Project>
";
            const string packageVersionTemplate = @"    <PackageVersion Include=""{0}"" Version=""{1}"" />";
            
            StringBuilder sb = new();

            foreach (var package in packages)
            {
                sb.AppendLine(string.Format(packageVersionTemplate, package.Name, package.Version));
            }

            return string.Format(emptyProjectTemplate, sb);
        }
    }
}
