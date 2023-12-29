﻿using Microsoft.VisualBasic;
using System.Text;
using System.Text.RegularExpressions;
using Gardener.Core;

namespace UpgradeRepo.Cpm
{
    internal static class ProjectFileHelpers
    {
        private const string PackageReferenceSingleLinePattern =
            @"<PackageReference\s+(?:[^>]*?\s+)?Include=""(?<name>[^""]+)""(?:[^>]*?\s+Version=""(?<version>[^""]+)"")?.*?/>";

        private const string PackageReferenceVersionPattern = " Version=\"[^\"]*\"";
        private const string PackageRefStartPattern = @"<PackageReference\s+Include=""(?<name>[^""]+)""";
        private const string VersionAttrPattern = @"Version=""(?<version>[^""]+)""";
        private const string VersionTagPattern = "<Version>(?<version>[^<]+)</Version>";
        private const string PackageReferenceCloseTagPattern = "</PackageReference>";
        private const string PackageReferenceClosedPattern = "<PackageReference[^>]*\\/>";
        private const string PackageReferenceStartTagPattern = @"<PackageReference\s+(?:[^>]*?\s+)?Include=""(?<name>[^""]+)"">";

        private static readonly Regex PackageRefStartRegex = new Regex(PackageRefStartPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionAttrRegex = new Regex(VersionAttrPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionTagRegex = new Regex(VersionTagPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceCloseTagRegex = new Regex(PackageReferenceCloseTagPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceClosedRegex = new Regex(PackageReferenceClosedPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PackageReferenceStartTagRegex = new Regex(PackageReferenceStartTagPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PackageReferenceSingleLineRegex =
            new Regex(PackageReferenceSingleLinePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PackageReferenceVersionRegex =
            new Regex(PackageReferenceVersionPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static async Task<List<Package>> GetPackagesAsync(string contents)
        {
            var packageReferences = new List<Package>();

            bool isWithinPackageRef = false;
            string? packageName = null;
            bool skippingCurrent = false;
            
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

                    skippingCurrent = false;

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

        public static async Task<string> UpdateVersions(string fileContent, Func<Package, string> versionResolver, bool endsWithNewLine)
        {
            string lineEnding = fileContent.DetermineLineEnding();
            var sb = new StringBuilder();

            bool isWithinPackageRef = false;
            string? packageName = null;
            bool skippingCurrent = false;
            var seenPackages = new HashSet<string>();

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

                    if (seenPackages.Contains(packageName))
                    {
                        // Skip adding this line if the package is already processed
                        skippingCurrent = true;
                        continue;
                    }

                    seenPackages.Add(packageName);
                    skippingCurrent = false;

                    var versionMatch = VersionAttrRegex.Match(line);
                    if (versionMatch.Success)
                    {
                        currentVersion = versionMatch.Groups["version"].Value;
                        string newVersion = versionResolver(new Package(packageName, currentVersion));

                        newVersion = string.IsNullOrEmpty(newVersion) ?
                            string.Empty :
                            $@" VersionOverride=""{newVersion}""";

                        var updatedLine = PackageReferenceVersionRegex.Replace(line, newVersion);
                        sb.Append(updatedLine);
                        sb.Append(lineEnding);
                        continue;
                    }
                }

                if (PackageReferenceCloseTagRegex.Match(line).Success)
                {
                    if (!skippingCurrent)
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
                            : $@"<VersionOverride>{newVersion}</VersionOverride>""";

                        var updatedLine = VersionTagRegex.Replace(line, newVersion);
                        if (!string.IsNullOrWhiteSpace(updatedLine))
                        {
                            sb.Append(updatedLine);
                            sb.Append(lineEnding);
                        }

                        continue;
                    }
                }

                sb.Append(line);
                sb.Append(lineEnding);
            }

            var length = sb.Length;
            if (!endsWithNewLine)
            {
                length -= lineEnding.Length;
            }

            return sb.ToString(0, length);
        }

        /// <summary>
        /// Write out a Directory.Packages.props file
        /// </summary>
        /// <param name="packages">Set of packages/versions</param>
        /// <returns>MSBuild file XML</returns>
        public static string GeneratePackageProps(IEnumerable<Package> packages, string lineEnding)
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

            foreach (var package in packages)
            {
                sb.Append(string.Format(PackageVersionTemplate, package.Name, package.VersionString, lineEnding));
            }

            return string.Format(EmptyProjectTemplate, sb);
        }
    }
}
