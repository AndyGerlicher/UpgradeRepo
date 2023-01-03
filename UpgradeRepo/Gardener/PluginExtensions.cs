// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Gardener.Core
{
    /// <summary>
    /// Contains various convenience extension methods for plugins to use.
    /// </summary>
    internal static class PluginExtensions
    {
        private const string WindowsStyleLineEnding = "\r\n";

        private const string LinuxStyleLineEnding = "\n";

        private const string WindowsStyleLineEndingReplacement = "$1" + WindowsStyleLineEnding;

        private static readonly Regex WindowsStyleLineEndingRegex = new Regex("\r\n", RegexOptions.Compiled);

        private static readonly Regex LinuxStyleLineEndingRegex = new Regex("([^\r])\n", RegexOptions.Compiled);

        /// <summary>
        /// Convert all line endings to the provided line ending.
        /// </summary>
        public static string WithLineEndings(this string content, string lineEnding)
            => lineEnding.Equals(LinuxStyleLineEnding, StringComparison.Ordinal)
                ? WindowsStyleLineEndingRegex.Replace(content, LinuxStyleLineEnding)
                : LinuxStyleLineEndingRegex.Replace(content, WindowsStyleLineEndingReplacement);

        /// <summary>
        /// Determine the line-endings used in the provided content.
        /// </summary>
        public static string DetermineLineEnding(this string content)
        {
            int numWindowsLineEndings = WindowsStyleLineEndingRegex.Matches(content).Count;
            int numLinuxLineEndings = LinuxStyleLineEndingRegex.Matches(content).Count;

            return numWindowsLineEndings >= numLinuxLineEndings
                ? WindowsStyleLineEnding
                : LinuxStyleLineEnding;
        }

        public static void ForEach<T>(this IEnumerable<T> collection, Action<T> action)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (action == null) throw new ArgumentNullException(nameof(action));

            foreach (T obj in collection)
                action(obj);
        }
    }
}
