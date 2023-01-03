using System.Text;

namespace UpgradeRepo.Cpv
{
    internal class ProjectFile
    {
        private readonly ProjFileInfo _file;
        private readonly List<Package> _packages = new();

        public string FilePath => _file.FullName;

        public ProjectFile(string file)
        {
            _file = new ProjFileInfo(file);
        }

        /// <summary>
        /// Read the file for PackageReference
        /// </summary>
        /// <returns></returns>
        public void ReadPackages()
        {
            foreach (var line in File.ReadAllLines(_file.FullName, _file.Encoding))
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
        /// <returns></returns>
        public void WritePackages(Func<Package, string> versionResolver)
        {
            bool dirty = false;

            StringBuilder sb = new StringBuilder();

            foreach (var line in File.ReadAllLines(_file.FullName, _file.Encoding))
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

                File.WriteAllText(_file.FullName, sb.ToString(0, length), _file.Encoding);
            }
        }

        public IEnumerable<Package> GetPackages()
        {
            return _packages;
        }

        
    }
}
