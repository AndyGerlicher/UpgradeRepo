namespace UpgradeRepo.Cpm;

internal class PackageReferenceLocation
{
    public Package Package { get; }

    public ProjectFile File { get; }

    public PackageReferenceLocation(Package package, ProjectFile file)
    {
        Package = package;
        File = file;
    }

    public override string ToString()
    {
        return $"{Package.Name} in {File.FilePath}";
    }
}