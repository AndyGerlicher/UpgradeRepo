namespace UpgradeRepo.Cpm;

internal record PackageReferenceLocation(Package Package, ProjectFile File)
{
    public override string ToString()
    {
        return $"{Package.Name} in {File.FilePath}";
    }
}