using Microsoft.VisualBasic;
using System.Text;
using System.Text.RegularExpressions;
using Gardener.Core;
using Gardener.Core.Packaging;
using NuGet.Versioning;

namespace UpgradeRepo.Cpm
{
    internal static class ProjectFileHelpers
    {
        private const string PackageReferenceVersionPattern = " Version\\s*=\\s*\"[^\"]*\"";
        private const string PackageRefStartPattern = @"<PackageReference\s+Include\s*=\s*""(?<name>[^""]+)""";
        private const string VersionAttrPattern = @"Version\s*=\s*""(?<version>[^""]+)""";
        private const string VersionTagPattern = "<Version>(?<version>[^<]+)</Version>";
        private const string PackageReferenceCloseTagPattern = "</PackageReference>";
        private const string PackageReferenceClosedPattern = "<PackageReference[^>]*\\/>";
        private const string EmptyPackageReferencePattern = """<PackageReference ([^>]+)\s*>\s*</PackageReference>""";
        private const string EmptyPackageReferenceReplacement = "<PackageReference $1/>";
        private const string PackageReferenceUpdatePattern = """<PackageReference\s+Update\s*=\s*"(?<name>[^"]+)"\s+(.*?)Version\s*=\s*"(?<version>[^"]+)"([^/>]*)(\/?)>""";
        private const string PackageReferenceUpdateReplacement = """<PackageReference Update="${name}" $1VersionOverride="${version}"$2$3>""";

        private static readonly Regex PackageRefStartRegex = new Regex(PackageRefStartPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionAttrRegex = new Regex(VersionAttrPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionTagRegex = new Regex(VersionTagPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceCloseTagRegex = new Regex(PackageReferenceCloseTagPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceClosedRegex = new Regex(PackageReferenceClosedPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceVersionRegex = new Regex(PackageReferenceVersionPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EmptyPackageReferenceRegex = new Regex(EmptyPackageReferencePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceUpdateRegex = new Regex(PackageReferenceUpdatePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static async Task<List<Package>> GetPackagesAsync(string contents)
        {
            var packageReferences = new List<Package>();

            bool isWithinPackageRef = false;
            string? packageName = null;

            using var sr = new StringReader(contents);
            while (await sr.ReadLineAsync() is { } line)
            {
                var packageRefMatch = PackageRefStartRegex.Match(line);
                string? currentVersion = null;
                if (packageRefMatch.Success)
                {
                    packageName = packageRefMatch.Groups["name"].Value;

                    if (!PackageReferenceClosedRegex.Match(line).Success)
                    {
                        // This was a start of a PackageReference, but not all of it!
                        isWithinPackageRef = true;
                    }

                    var versionMatch = VersionAttrRegex.Match(line);
                    if (versionMatch.Success)
                    {
                        currentVersion = versionMatch.Groups["version"].Value;
                        packageReferences.Add(new Package(packageName, currentVersion));
                        continue;
                    }
                }

                if (PackageReferenceCloseTagRegex.Match(line).Success)
                {
                    isWithinPackageRef = false;
                    continue;
                }

                if (isWithinPackageRef)
                {
                    var versionMatch = VersionTagRegex.Match(line);
                    if (versionMatch.Success)
                    {
                        currentVersion = versionMatch.Groups["version"].Value;
                        packageReferences.Add(new Package(packageName, currentVersion));
                    }
                }
            }

            return packageReferences;
        }

        public static async Task<(string? NewFileContents, bool IsDirty)> UpdateVersionsAsync(string fileContent, ProjFileInfo fileInfo, Func<Package, string> versionResolver)
        {
            bool dirty = false;
            string lineEnding = fileInfo.LineEndings;
            var sb = new StringBuilder();

            bool isWithinPackageRef = false;
            string? packageName = null;
            bool skippingCurrent = false;
            var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var sr = new StringReader(fileContent);
            while (await sr.ReadLineAsync() is { } line)
            {
                var packageRefMatch = PackageRefStartRegex.Match(line);
                string? currentVersion = null;
                if (packageRefMatch.Success)
                {
                    packageName = packageRefMatch.Groups["name"].Value;

                    if (!PackageReferenceClosedRegex.Match(line).Success)
                    {
                        // This was a start of a PackageReference, but not all of it!
                        isWithinPackageRef = true;
                    }

                    if (!seenPackages.Add(packageName))
                    {
                        // Skip adding this line if the package is already processed
                        skippingCurrent = true;
                        continue;
                    }

                    skippingCurrent = false;

                    var versionMatch = VersionAttrRegex.Match(line);
                    if (versionMatch.Success)
                    {
                        currentVersion = versionMatch.Groups["version"].Value;
                        string newVersion = versionResolver(new Package(packageName, currentVersion));

                        newVersion = string.IsNullOrEmpty(newVersion)
                            ? string.Empty
                            : $@" VersionOverride=""{newVersion}""";

                        var updatedLine = PackageReferenceVersionRegex.Replace(line, newVersion);
                        sb.Append(updatedLine);
                        sb.Append(lineEnding);
                        dirty = true;
                        continue;
                    }
                }

                if (PackageReferenceCloseTagRegex.Match(line).Success)
                {
                    if (skippingCurrent)
                    {
                        dirty = true;
                    }
                    else
                    {
                        sb.Append(line);
                        sb.Append(lineEnding);
                    }

                    isWithinPackageRef = false;
                    skippingCurrent = false;
                    continue;
                }

                if (isWithinPackageRef)
                {
                    var versionMatch = VersionTagRegex.Match(line);
                    if (versionMatch.Success)
                    {
                        currentVersion = versionMatch.Groups["version"].Value;
                        string newVersion = versionResolver(new Package(packageName, currentVersion));

                        newVersion = string.IsNullOrEmpty(newVersion)
                            ? string.Empty
                            : $@"<VersionOverride>{newVersion}</VersionOverride>";

                        var updatedLine = VersionTagRegex.Replace(line, newVersion);
                        if (!string.IsNullOrWhiteSpace(updatedLine))
                        {
                            sb.Append(updatedLine);
                            sb.Append(lineEnding);
                        }

                        dirty = true;
                        continue;
                    }
                }

                sb.Append(line);
                sb.Append(lineEnding);
            }

            var length = sb.Length;

            // When the file doesn't end in new line, remove the one we just added
            if (!fileInfo.EndsWithNewLine)
            {
                length = sb.Length - lineEnding.Length;
            }

            var contents = sb.ToString(0, length);
            contents = EmptyPackageReferenceRegex.Replace(contents, EmptyPackageReferenceReplacement);

            if (PackageReferenceUpdateRegex.IsMatch(contents))
            {
                dirty = true;
                contents = PackageReferenceUpdateRegex.Replace(contents, PackageReferenceUpdateReplacement);
            }

            return (contents, dirty);
        }

        public static List<Package> GetPackageUpdateVersions(string contents)
        {
            var packageReferences = new List<Package>();
            var matches = PackageReferenceUpdateRegex.Matches(contents);
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    var name = match.Groups["name"].Value;
                    var version = match.Groups["version"].Value;

                    var package = new Package(name, version);
                    if (package.VersionType == PackageVersionType.NuGetVersion)
                    {
                        packageReferences.Add(package);
                    }
                }
            }

            return packageReferences;
        }
    }
}
