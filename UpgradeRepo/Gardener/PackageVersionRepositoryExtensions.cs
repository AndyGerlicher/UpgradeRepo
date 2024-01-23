// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace Gardener.Core.Packaging;

internal static class PackageVersionRepositoryExtensions
{
    public static async Task<NuGetVersion?> GetLatestPackageVersionAsync(this IPackageVersionRepository packageVersionRepository, NugetPackage package, CancellationToken cancellationToken)
        => (await packageVersionRepository.GetPackageVersionsAsync(package, cancellationToken)).Max();

    public static async Task<NuGetVersion?> GetLatestPackageVersionAsync(this IPackageVersionRepository packageVersionRepository, AzureArtifactsFeed feed, NugetPackage package, CancellationToken cancellationToken)
        => (await packageVersionRepository.GetPackageVersionsAsync(feed, package, cancellationToken)).Max();
}
