using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace UpgradeRepo.Cpv
{
    internal class Package
    {
        public string Name { get; }
        public NuGetVersion Version { get; }

        public Package(string name, NuGetVersion version)
        {
            if (string.IsNullOrEmpty(name)) Debugger.Launch();
            Name = name;
            Version = version;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Package package)
            {
                return false;
            }

            return Name == package.Name && Version.Equals(package.Version);
        }

        public override int GetHashCode()
        {
            return (Name + Version).GetHashCode();
        }
    }
}
