using Gardener.Core;
using Gardener.Core.Json;
using Gardener.Core.MSBuild;
using System.Text;
using System.Text.RegularExpressions;

namespace UpgradeRepo.LegacyCpv
{
    internal class LegacyCpvPlugin : IUpgradePlugin
    {
        public Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            return fileSystem.FileExistsAsync(Path.Combine(options.Path, "Packages.props"));
        }

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            var legacyPath = Path.Combine(options.Path, "Packages.props");
            var newPackagesPropsPath = Path.Combine(options.Path, "Directory.Packages.props");
            var dbPropsPath = Path.Combine(options.Path, "Directory.Build.props");
            var globalJsonPath = Path.Combine(options.Path, "global.json");

            Console.WriteLine(" * Renaming Packages.props -> Directory.Packages.props");
            await fileSystem.RenameFileAsync(legacyPath, newPackagesPropsPath);
            var dbPropsFile = await MSBuildFile.ReadAsync(fileSystem, dbPropsPath);
            var newPackagesPropsFile = await MSBuildFile.ReadAsync(fileSystem, newPackagesPropsPath);
            var globalJsonFile = await GlobalJsonFile.ReadAsync(fileSystem, globalJsonPath);

            EnableFeature(dbPropsFile);

            if (!FixPackageReferenceUpdate(newPackagesPropsFile))
            {
                throw new Exception("No Package Versions updated!");
            }

            Console.WriteLine(" * Removing existing references to CPV package");
            RemoveSdkEnable(dbPropsFile);
            RemoveSdkFromGlobalJson(globalJsonFile);
            
            await fileSystem.WriteAllTextAsync(newPackagesPropsPath, newPackagesPropsFile.Content);
            await fileSystem.WriteAllTextAsync(dbPropsPath, dbPropsFile.Content);
            await fileSystem.WriteAllTextAsync(globalJsonPath, globalJsonFile.Content);
            
            Console.WriteLine("Upgrade complete");
            return true;
        }

        /// <summary>
        /// Add EnableCentralPackageVersions property to the specified MSBuild file
        /// </summary>
        /// <param name="msBuildFile"></param>
        /// <returns>new file contents</returns>
        public void EnableFeature(MSBuildFile msBuildFile)
        {
            Console.WriteLine(" * Setting EnableCentralPackageVersions Property");
            msBuildFile.RemoveProperty("EnableCentralPackageVersions");
            msBuildFile.SetProperties(new[] { new MSBuildFile.Property("ManagePackageVersionsCentrally", "true") });
        }

        /// <summary>
        /// Remove Microsoft.Build.CentralPackageVersions from global.json.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public bool RemoveSdkFromGlobalJson(GlobalJsonFile file)
        {
            // This isn't super critical. It doesn't do any harm leaving it in the file.
            file.TryRemoveMsBuildSdk("Microsoft.Build.CentralPackageVersions");
            
            return true;
        }

        public bool RemoveSdkEnable(MSBuildFile msBuildFile)
        {
            // TODO: Not ideal here, way too specific
            msBuildFile.RemoveTopLevelXml("  <Sdk Name=\"Microsoft.Build.CentralPackageVersions\" />");

            var newContents = msBuildFile.Content;

            // Be much more permissive in detecting if we missed it.
            if (Regex.IsMatch(msBuildFile.Content, @"Name=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(msBuildFile.Content, @"SDK=""Microsoft.Build.CentralPackageVersions""", RegexOptions.IgnoreCase))
            {
                throw new Exception("Couldn't remove legacy SDK declaration");
            }

            return true;
        }

        public bool FixPackageReferenceUpdate(MSBuildFile packagesPropsFile)
        {
            Console.WriteLine(" * Updating ItemGroup PackageReference -> PackageVersion");

            // Simple regex line by line. Won't work if the Update is on a new line, which is valid
            // XML but I doubt exists anywhere.
            const string packageVersionUpdatePattern = @"<PackageReference\s+Update=";
            var dirty = false;
            var sb = new StringBuilder();
            StringReader reader = new StringReader(packagesPropsFile.Content);
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
