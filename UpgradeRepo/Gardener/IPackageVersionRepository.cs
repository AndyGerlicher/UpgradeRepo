// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace Gardener.Core.Packaging;

public interface IPackageVersionRepository
{
    /// <summary>
    /// Gets the version of a package available on NuGet.org.
    /// </summary>
    /// <param name="package">The package to query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The latest version of the package, or null.</returns>
    Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(NugetPackage package, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the version of a package available on a feed.
    /// </summary>
    /// <param name="feed">The feed to query.</param>
    /// <param name="package">The package to query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The latest version of the package, or null.</returns>
    Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(AzureArtifactsFeed feed, NugetPackage package, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the best match version for a package.
    /// </summary>
    /// <param name="feed">The feed to query.</param>
    /// <param name="packageName">The package to query.</param>
    /// <param name="versionRange">Package version Range.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The latest version of the package, or null.</returns>
    Task<NuGetVersion?> FindBestMatchPackageVersionAsync(AzureArtifactsFeed feed, string packageName, VersionRange versionRange, CancellationToken cancellationToken);
}