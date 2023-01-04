Prototype tool to modify/upgrade a repo. 

## Central Package Management
Manage NuGet packages centrally. See [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/Central-Package-Management) for details.

Usage:
* Run `RepoUpgrade cpv` at the root of your repo.
* Check files for accuracy. If you have multiple versions specified, the minimum version will go into `Directory.Packages.props` and the leaf nodes will override.


## Upgrade Legacy CPV
If you're currently using the MSBuild SDK ([Microsoft.Build.CentralPackageVersions](https://github.com/microsoft/MSBuildSdks/tree/main/src/CentralPackageVersions)), this can attempt to upgrade you.

Usage:
* Run `RepoUpgrade legacycpv` at the root of your repo.
* `Packages.props` is renamed to `Directory.Packages.props`.

## Build RSP
Add better build defaults (`Directory.Build.rsp`).

Usage:
* Run `repoUpgrade rsp` at the root of your repo.
* `git add Directory.Build.rsp -f`
