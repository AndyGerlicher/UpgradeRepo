using NuGet.Versioning;

namespace UpgradeRepo.Cpm
{
    internal class Package : IComparable<Package>
    {
        public string Name { get; }

        public NuGetVersion? NuGetVersion { get; }

        public string VersionString { get; }

        public PackageVersionType VersionType { get; }

        public Package(string name, string version)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));

            if (string.IsNullOrEmpty(version))
            {
                throw new ArgumentNullException($"Package {Name} had a null/empty version!");
            }

            if (version.Contains('*'))
            {
                VersionType = PackageVersionType.Wildcard;
                VersionString = version;
            }
            else if (version.StartsWith("$(", StringComparison.OrdinalIgnoreCase))
            {
                VersionType = PackageVersionType.MSBuildProperty;
                VersionString = version;
            }
            else
            {
                VersionType = PackageVersionType.NuGetVersion;
                NuGetVersion = new NuGetVersion(version);
                VersionString = NuGetVersion.ToString();
            }
        }

        public int CompareTo(Package? other)
        {
            if (other == null)
            {
                return -1;
            }

            // Compare by Name first
            int nameComparison = string.Compare(Name, other.Name, StringComparison.Ordinal);
            if (nameComparison != 0)
            {
                return nameComparison;
            }

            // Compare by VersionType
            if (VersionType != other.VersionType)
            {
                // Treat WildCard or MSBuildProperty as greater than NugetVersion
                if (VersionType == PackageVersionType.NuGetVersion && (other.VersionType == PackageVersionType.Wildcard || other.VersionType == PackageVersionType.MSBuildProperty))
                {
                    return -1;
                }

                if ((VersionType == PackageVersionType.Wildcard || VersionType == PackageVersionType.MSBuildProperty) && other.VersionType == PackageVersionType.NuGetVersion)
                {
                    return 1;
                }

                // If both are either WildCard or MSBuildProperty, sort by name
                return nameComparison;
            }

            // If both are NuGetVersion, then compare by Version string
            if (VersionType == PackageVersionType.NuGetVersion)
            {
                return NuGetVersion!.CompareTo(other.NuGetVersion);
            }

            // If VersionTypes are same but not NuGetVersion, they are considered equal
            return 0;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Package package)
            {
                return false;
            }

            return Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase) &&
                   VersionType == package.VersionType &&
                   VersionString.Equals(package.VersionString, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return (Name + VersionString).GetHashCode();
        }
    }

    internal enum PackageVersionType
    {
        NuGetVersion,
        Wildcard,
        MSBuildProperty,
    }
}
