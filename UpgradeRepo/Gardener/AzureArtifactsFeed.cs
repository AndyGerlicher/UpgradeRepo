// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;

namespace Gardener.Core.Packaging;

public readonly record struct AzureArtifactsFeed(string AccountName, string? ProjectName, string FeedName)
{
    private static readonly Regex FeedRegex = new(
        @"^https://(pkgs\.dev\.azure\.com/(?<AccountName>[^/]+)|(?<AccountName>[^\.]*)\.pkgs\.visualstudio\.com)/(DefaultCollection/)?((?<ProjectName>[^/]*)/)?(_apis/packaging|_packaging)/(?<FeedName>.*)/nuget(/v3)?/index.json$",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);

    public static bool TryParseFromUrl(string feedUrl, out AzureArtifactsFeed feed)
    {
        Match match = FeedRegex.Match(feedUrl);
        if (!match.Success)
        {
            feed = default;
            return false;
        }

        string accountName = match.Groups["AccountName"].Value;
        string? projectName = match.Groups["ProjectName"].Value;
        string feedName = match.Groups["FeedName"].Value;

        // The regex will return an empty string if there was no match, so null it out
        if (string.IsNullOrEmpty(projectName))
        {
            projectName = null;
        }

        feed = new AzureArtifactsFeed(accountName, projectName, feedName);
        return true;
    }

    public static IReadOnlyList<AzureArtifactsFeed> ParseNuGetConfig(string nugetConfigContent)
    {
        XmlDocument xmlDocument = new();
        try
        {
            xmlDocument.LoadXml(nugetConfigContent);
        }
        catch (XmlException)
        {
            return Array.Empty<AzureArtifactsFeed>();
        }

        if (xmlDocument.DocumentElement == null)
        {
            return Array.Empty<AzureArtifactsFeed>();
        }

        XmlNodeList? feedUrlNodes = xmlDocument.DocumentElement.SelectNodes("/configuration/packageSources/add/@value");
        if (feedUrlNodes == null || feedUrlNodes.Count == 0)
        {
            return Array.Empty<AzureArtifactsFeed>();
        }

        List<AzureArtifactsFeed> feeds = new(feedUrlNodes.Count);
        foreach (XmlNode? node in feedUrlNodes)
        {
            if (node?.Value != null && TryParseFromUrl(node.Value, out AzureArtifactsFeed feed))
            {
                feeds.Add(feed);
            }
        }

        return feeds;
    }

    public override string ToString() => ProjectName != null
        ? $"azure-feed://{AccountName}/{ProjectName}/{FeedName}"
        : $"azure-feed://{AccountName}/{FeedName}";
}
