// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System.Threading.Tasks;

namespace Gardener.Core
{
    /// <summary>
    /// A base class for managing a structured file.
    /// </summary>
    internal abstract class StructuredFileBase
    {
        protected StructuredFileBase(IFileSystem fileSystem, string filePath, string content)
        {
            FilePath = filePath;
            Content = content;

            LineEnding = content.DetermineLineEnding();
        }

        public string FilePath { get; }

        internal string Content { get; set; }

        public string LineEnding { get; }
    }
}
