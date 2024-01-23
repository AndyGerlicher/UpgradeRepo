using NuGet.Versioning;

namespace UpgradeRepo.Cpm
{
    internal class Package : IComparable<Package>
    {
        public string Name { get; }

        public NuGetVersion? NuGetVersion { get; }

        public string VersionString { get; }

        public PackageVersionType VersionType { get; }

        public Package(string? name, string? version)
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
            else if (version.Contains('[') || version.Contains(']'))
            {
                VersionType = PackageVersionType.VersionRange;
                VersionString = version;
            }
            else if (version.Contains("$(", StringComparison.OrdinalIgnoreCase))
            {
                VersionType = PackageVersionType.MSBuildProperty;
                VersionString = version;
            }
            else if (NuGetVersion.TryParse(version, out var nugetVersion))
            {
                VersionType = PackageVersionType.NuGetVersion;
                NuGetVersion = nugetVersion;
                VersionString = NuGetVersion.ToString();
            }
            else
            {
                VersionType = PackageVersionType.Unknown;
                VersionString = version;
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
                return ((int)VersionType).CompareTo((int)other.VersionType);
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
        Unknown = 0,
        NuGetVersion = 1,
        Wildcard = 2,
        VersionRange = 3,
        MSBuildProperty = 4,
    }
}
