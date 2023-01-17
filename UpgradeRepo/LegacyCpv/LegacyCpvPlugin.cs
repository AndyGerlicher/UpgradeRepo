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

        private static readonly string LegacyCpvSdkEnablePattern = @$"\s*<Sdk Name=""{LegacyCpvSdkName}""\s*(Version=""[\d|\.|\w|-]*"")?\s*/>";
        private readonly ILogger _logger;

        public LegacyCpvPlugin(ILogger logger)
        {
            _logger = logger;
        }
        public Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            return fileSystem.FileExistsAsync(Path.Combine(options.Path, "Packages.props"));
        }

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            if (!(await fileSystem.FileExistsAsync(LegacyPackagesPropsFilePath)))
            {
                _logger.LogError("No Legacy Packages.props file");
                return false;
            }

            var dbPropsFile = await MSBuildFile.ReadAsync(fileSystem, DbBuildPropsFile);
            var dbTargetsFile = await MSBuildFile.ReadAsync(fileSystem, DbBuildTargetsFile);
            var pacakgesPropsFile = await MSBuildFile.ReadAsync(fileSystem, LegacyPackagesPropsFilePath);
            var globalJsonFile = await GlobalJsonFile.ReadAsync(fileSystem, GlobalJsonFilePath);

            EnableFeature(dbPropsFile);

            if (!FixPackageReferenceUpdate(pacakgesPropsFile))
            {
                _logger.LogError("No package versions were updated");
                return false;
            }

            _logger.LogDebug("Removing existing references to CPV package");
            if (!RemoveSdkEnable(dbTargetsFile))
            {
                _logger.LogError("Couldn't remove SDK from Directory.Build.targets");
                return false;
            }

            RemoveSdkFromGlobalJson(globalJsonFile);

            await fileSystem.WriteAllTextAsync(DbPackagesPropsFile, pacakgesPropsFile.Content);
            await fileSystem.DeleteFileAsync(LegacyPackagesPropsFilePath);
            await fileSystem.WriteAllTextAsync(DbBuildPropsFile, dbPropsFile.Content);
            await fileSystem.WriteAllTextAsync(DbBuildTargetsFile, dbTargetsFile.Content);
            await fileSystem.WriteAllTextAsync(GlobalJsonFilePath, globalJsonFile.Content);

            return true;
        }

        /// <summary>
        /// Add EnableCentralPackageVersions property to the specified MSBuild file.
        /// </summary>
        /// <param name="msBuildFile">MSBuild file to operate on.</param>
        internal void EnableFeature(MSBuildFile msBuildFile)
        {
            _logger.LogDebug("Setting EnableCentralPackageVersions Property");
            msBuildFile.RemoveProperty("EnableCentralPackageVersions");
            msBuildFile.SetProperties(new[] { new MSBuildFile.Property("ManagePackageVersionsCentrally", "true") });
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

        internal bool RemoveSdkEnable(MSBuildFile msBuildFile)
        {
            // TODO: Not ideal here, way too specific
            var sdkTextMatch = Regex.Match(msBuildFile.Content, LegacyCpvSdkEnablePattern, RegexOptions.IgnoreCase);

            if (sdkTextMatch.Success)
            {
                msBuildFile.RemoveTopLevelXml(sdkTextMatch.Value);

                // Be much more permissive in detecting if we missed it.
                return !Regex.IsMatch(msBuildFile.Content, @$"Name=""{LegacyCpvSdkName}""", RegexOptions.IgnoreCase) &&
                       !Regex.IsMatch(msBuildFile.Content, @$"SDK=""{LegacyCpvSdkName}""", RegexOptions.IgnoreCase);
            }

            return false;
        }

        internal bool FixPackageReferenceUpdate(MSBuildFile packagesPropsFile)
        {
            _logger.LogDebug("Updating ItemGroup PackageReference -> PackageVersion");

            // Simple regex line by line. Won't work if the Update is on a new line, which is valid
            // XML but I doubt exists anywhere.
            const string packageVersionUpdatePattern = @"<PackageReference\s+Update=";
            var dirty = false;
            var sb = new StringBuilder();
            var reader = new StringReader(packagesPropsFile.Content);
            var line = reader.ReadLine();

            while (line != null)
            {
                var updatedLine = line;
                if (Regex.IsMatch(line, packageVersionUpdatePattern))
                {
                    updatedLine = Regex.Replace(line, packageVersionUpdatePattern, "<PackageVersion Include=");
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
    }
}
