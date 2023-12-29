using Gardener.Core;
using System.Data.SqlTypes;
using System.Text;
using System.Xml;

namespace UpgradeRepo.Cpm
{
    internal class ProjectFile(IFileSystem fileSystem, string file)
    {
        private readonly ProjFileInfo _file = new(fileSystem, file);
        private readonly List<Package> _packages = new();
        private readonly IFileSystem _fileSystem = fileSystem;

        public string FilePath => _file.FullName;

        /// <summary>
        /// Read the file for PackageReference
        /// </summary>
        public async Task ReadPackagesAsync()
        {
            string contents = await _fileSystem.ReadAllTextAsync(_file.FullName);
            _packages.AddRange(await ProjectFileHelpers.GetPackagesAsync(contents));
        }

        /// <summary>
        /// Write out PackageVersion lines with no version (or override)
        /// </summary>
        public async Task WritePackagesAsync(Func<Package, string> versionResolver)
        {
            string contents = await _fileSystem.ReadAllTextAsync(_file.FullName);
            var newContents = await ProjectFileHelpers.UpdateVersions(contents, versionResolver, _file.EndsWithNewLine);

            if (!contents.Equals(newContents, StringComparison.Ordinal))
            {
                // Since we're editing the file pretty heavily, we should verify it's still valid xml.
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(newContents);

                await _fileSystem.WriteAllTextAsync(_file.FullName, newContents);
            }
        }

        public IEnumerable<Package> GetPackages()
        {
            return _packages;
        }
    }
}
