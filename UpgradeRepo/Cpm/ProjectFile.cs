using Gardener.Core;
using System.Text;

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
            foreach (var line in contents.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                if (line.LineContainsPackageReference())
                {
                    _packages.Add(ProjectFileHelpers.GetPackageFromLine(line));
                }
            }
        }

        /// <summary>
        /// Write out PackageVersion lines with no version (or override)
        /// </summary>
        public async Task WritePackagesAsync(Func<Package, string> versionResolver)
        {
            bool dirty = false;
            StringBuilder sb = new StringBuilder();

            string contents = await _fileSystem.ReadAllTextAsync(_file.FullName);

            string? line;
            using var sr = new StringReader(contents);

            while ((line = await sr.ReadLineAsync()) != null)
            {
                var line2 = line;

                if (line.LineContainsPackageReference())
                {
                    line2 = ProjectFileHelpers.UpdateVersion(line, versionResolver);
                    dirty = true;
                }

                sb.AppendLine(line2);
            }

            if (dirty)
            {
                var length = sb.Length;

                if (!_file.EndsWithNewLine)
                {
                    length -= Environment.NewLine.Length;
                }

                await _fileSystem.WriteAllTextAsync(_file.FullName, sb.ToString(0, length));
            }
        }

        public IEnumerable<Package> GetPackages()
        {
            return _packages;
        }
    }
}
