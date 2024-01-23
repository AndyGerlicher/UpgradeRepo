using Gardener.Core;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace UpgradeRepo.Cpm
{
    internal class ProjectFile(IFileSystem fileSystem, ILogger logger, string file)
    {
        private readonly ProjFileInfo _file = new(fileSystem, file);
        private readonly List<Package> _packages = new();
        private readonly List<Package> _supplementalPackages = new();
        private readonly IFileSystem _fileSystem = fileSystem;
        private readonly ILogger _logger = logger;
        private string? _fileContents;

        public string FilePath => _file.FullName;

        /// <summary>
        /// Read the file for PackageReference
        /// </summary>
        public async Task ReadPackagesAsync()
        {
            _fileContents = await _fileSystem.ReadAllTextAsync(_file.FullName);

            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(_fileContents);
                _packages.AddRange(await ProjectFileHelpers.GetPackagesAsync(_fileContents!));
                _supplementalPackages.AddRange(ProjectFileHelpers.GetPackageUpdateVersions(_fileContents!));
            }
            catch (XmlException e)
            {
                _logger.LogWarning($"Invalid proj file! File: {file}, Error: {e.Message}");
                _fileContents = null;
            }
        }

        /// <summary>
        /// Write out PackageVersion lines with no version (or override)
        /// </summary>
        public async Task<bool> WritePackagesAsync(Func<Package, string> versionResolver)
        {
            bool wroteFiles = false;

            if (string.IsNullOrEmpty(_fileContents))
            {
                return wroteFiles;
            }

            (string? newContents, bool dirty) = await ProjectFileHelpers.UpdateVersionsAsync(_fileContents, _file, versionResolver);

            if (dirty && !string.IsNullOrEmpty(newContents))
            {
                // Since we're editing the file pretty heavily, we should verify it's still valid xml and throw XmlException if not.
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(newContents);

                await _fileSystem.WriteAllTextAsync(_file.FullName, newContents);
                wroteFiles = true;
            }

            return wroteFiles;
        }

        public IEnumerable<Package> GetPackages()
        {
            return _packages;
        }

        public IEnumerable<Package> GetSupplementalPackages()
        {
            return _supplementalPackages;
        }
    }
}
