using System.ComponentModel.Design;
using System.Reflection.Metadata.Ecma335;
using Gardener.Core;
using Gardener.Core.Json;
using Gardener.Core.MSBuild;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace UpgradeRepo.LegacyCpv
{
    internal class LegacyCpvPlugin : IUpgradePlugin
    {
        private const string LegacyPackagesPropsFilePath = "Packages.props";
        private const string DbPackagesPropsFile = "Directory.Packages.props";
        private const string DbBuildPropsFile = "Directory.Build.props";
        private const string DbBuildTargetsFile = "Directory.Build.targets";
        private const string GlobalJsonFilePath = "global.json";
        private const string LegacyCpvSdkName = "Microsoft.Build.CentralPackageVersions";

        private const string PropertyComment =
            @"<!-- Enable Central Package Management unless the project is using packages.config or is a project that does not support PackageReference -->";
        private const string PropertyCondition = @"'$(ManagePackageVersionsCentrally)' == ''
{0}And !Exists('$(MSBuildProjectDirectory)\packages.config')
{0}And '$(MSBuildProjectExtension)' != '.vcxproj'
{0}And '$(MSBuildProjectExtension)' != '.ccproj'
{0}And '$(MSBuildProjectExtension)' != '.nuproj'";

        private const string LegacyPackageReferenceVersionDefinition = @"<PackageReference\s+Update=";
        private const string CpmPackageReferenceVersionDefinition = "<PackageVersion Include=";

        private static readonly Regex PackageReferenceRegex = new(LegacyPackageReferenceVersionDefinition, RegexOptions.Compiled);

        private static readonly string LegacyCpvSdkEnablePattern = @$"\s*<Sdk Name=""{LegacyCpvSdkName}""\s*(Version=""[\d|\.|\w|-]*"")?\s*/>";

        private readonly ILogger _logger;

        public LegacyCpvPlugin(ILogger logger)
        {
            _logger = logger;
        }
        public Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            return CheckRequiredFiles(fileSystem);
        }

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            if (!await CheckRequiredFiles(fileSystem))
            {
                return false;
            }

            var dbPropsFile = await MSBuildFile.ReadAsync(fileSystem, DbBuildPropsFile);
            var dbTargetsFile = await MSBuildFile.ReadAsync(fileSystem, DbBuildTargetsFile);
            var pacakgesPropsFile = await MSBuildFile.ReadAsync(fileSystem, LegacyPackagesPropsFilePath);

            EnableCpmFeature(dbPropsFile);

            if (!FixPackageReferenceUpdate(pacakgesPropsFile))
            {
                _logger.LogError("No package versions were updated");
                return false;
            }

            _logger.LogDebug("Removing existing references to CPV package");
            if (!DisableLegacyFeature(dbTargetsFile, dbPropsFile))
            {
                _logger.LogError("Couldn't remove SDK from Directory.Build.targets");
                return false;
            }

            await fileSystem.WriteAllTextAsync(DbPackagesPropsFile, pacakgesPropsFile.Content);
            await fileSystem.DeleteFileAsync(LegacyPackagesPropsFilePath);
            await fileSystem.WriteAllTextAsync(DbBuildPropsFile, dbPropsFile.Content);
            await fileSystem.WriteAllTextAsync(DbBuildTargetsFile, dbTargetsFile.Content);

            // Optionally remove global.json file contents
            if (await fileSystem.FileExistsAsync(GlobalJsonFilePath))
            {
                var globalJsonFile = await GlobalJsonFile.ReadAsync(fileSystem, GlobalJsonFilePath);
                RemoveSdkFromGlobalJson(globalJsonFile);
                await fileSystem.WriteAllTextAsync(GlobalJsonFilePath, globalJsonFile.Content);
            }

            return true;
        }

        /// <summary>
        /// Add ManagePackageVersionsCentrally property to the specified MSBuild file.
        /// </summary>
        /// <param name="dbPropsFile">MSBuild file to operate on.</param>
        internal void EnableCpmFeature(MSBuildFile dbPropsFile)
        {
            _logger.LogDebug("Setting EnableCentralPackageVersions Property");

            // Add the ManagePackageVersionsCentrally with condition to disable in certain conditions.
            string conditionText = string.Format(PropertyCondition, dbPropsFile.PropertyGroupIndentation + dbPropsFile.PropertyIndentation);
            dbPropsFile.SetProperties(new[]
            {
                new MSBuildFile.Property("ManagePackageVersionsCentrally", "true", conditionText)
            });

            // A bit of a hack, but add a comment above the property we just added.
            dbPropsFile.Content = dbPropsFile.Content.Replace(
                dbPropsFile.PropertyIndentation + "<ManagePackageVersionsCentrally ",
                dbPropsFile.LineEnding + dbPropsFile.PropertyIndentation + PropertyComment + dbPropsFile.LineEnding + dbPropsFile.PropertyIndentation + "<ManagePackageVersionsCentrally ");
        }

        /// <summary>
        /// Remove Microsoft.Build.CentralPackageVersions from global.json.
        /// </summary>
        /// <param name="file">Global.json file to operate on.</param>
        /// <returns>True when the SDK was able to be removed.</returns>
        internal bool RemoveSdkFromGlobalJson(GlobalJsonFile file)
        {
            // This isn't super critical. It doesn't do any harm leaving it in the file.
            file.TryRemoveMsBuildSdk(LegacyCpvSdkName);
            if (file.Content.Contains(LegacyCpvSdkName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Unable to remove SDK from global.json");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Disable Existing CPV feature (property + SDK)
        /// </summary>
        /// <param name="dbTargetsFile">Directory.Build.targets file to operate on.</param>
        /// <param name="dbPropsFile">Directory.Build.props file to operate on.</param>
        /// <returns>True when the SDK could be removed (property may be left behind).</returns>
        internal bool DisableLegacyFeature(MSBuildFile dbTargetsFile, MSBuildFile dbPropsFile)
        {
            // If the property isn't removed, it won't harm anything.
            _logger.LogDebug("Removing EnableCentralPackageVersions property from Directory.Build.props");
            dbPropsFile.RemoveProperty("EnableCentralPackageVersions");

            _logger.LogDebug("Removing SDK declaration from Directory.Build.targets");
            var sdkTextMatch = Regex.Match(dbTargetsFile.Content, LegacyCpvSdkEnablePattern, RegexOptions.IgnoreCase);

            if (sdkTextMatch.Success)
            {
                dbTargetsFile.RemoveTopLevelXml(sdkTextMatch.Value);

                // Be much more permissive in detecting if we missed it.
                return !Regex.IsMatch(dbTargetsFile.Content, @$"Name=""{LegacyCpvSdkName}""", RegexOptions.IgnoreCase) &&
                       !Regex.IsMatch(dbTargetsFile.Content, @$"SDK=""{LegacyCpvSdkName}""", RegexOptions.IgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Change PackageReference to PackageVersion.
        /// </summary>
        /// <param name="packagesPropsFile">Packages.props file to operate on.</param>
        /// <returns>True when at least one update was made.</returns>
        internal bool FixPackageReferenceUpdate(MSBuildFile packagesPropsFile)
        {
            _logger.LogDebug("Updating ItemGroup PackageReference -> PackageVersion");

            // Simple regex line by line. Won't work if the Update is on a new line, which is valid
            // XML but I doubt exists anywhere.
            var dirty = false;
            var sb = new StringBuilder(packagesPropsFile.Content.Length);
            var reader = new StringReader(packagesPropsFile.Content);
            var line = reader.ReadLine();

            while (line != null)
            {
                var updatedLine = line;
                if (PackageReferenceRegex.IsMatch(line))
                {
                    updatedLine = PackageReferenceRegex.Replace(line, CpmPackageReferenceVersionDefinition);
                    dirty = true;
                }

                sb.AppendLine(updatedLine);
                line = reader.ReadLine();
            }

            if (dirty)
            {
                packagesPropsFile.Content = sb.ToString();
            }

            return dirty;
        }

        private async Task<bool> CheckRequiredFiles(IFileSystem fileSystem)
        {
            bool missingFiles = false;

            if (!await fileSystem.FileExistsAsync(DbBuildPropsFile))
            {
                missingFiles = true;
                _logger.LogError("Could not find Directory.Build.props file.");
            }
            if (!await fileSystem.FileExistsAsync(DbBuildTargetsFile))
            {
                missingFiles = true;
                _logger.LogError("Could not find Directory.Build.targetsfile.");
            }
            if (!await fileSystem.FileExistsAsync(LegacyPackagesPropsFilePath))
            {
                missingFiles = true;
                _logger.LogError("Could not find legacy Packages.props file.");
            }

            return !missingFiles;
        }
    }
}
