// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Gardener.Core.Json
{
    /// <summary>
    /// This class represents an global.json file (a project, props, or targets file) and provides various common operations.
    /// </summary>
    internal class GlobalJsonFile : StructuredFileBase
    {
        public const string FileLocation = "global.json";

        private static readonly JsonDocumentOptions JsonDocumentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        /// <summary>
        /// Gets or sets the deserialized data contract object model for the global.json file.
        /// </summary>
        private JsonDocument _document;

        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalJsonFile"/> class from the provided file path.
        /// </summary>
        public GlobalJsonFile(IFileSystem fileSystem, string filePath, string content)
            : base(fileSystem, filePath, content)
        {
            _document = JsonDocument.Parse(content, JsonDocumentOptions);
        }

        /// <summary>
        /// Reads an <see cref="MSBuildFile"/> from the provided file path.
        /// </summary>
        /// <exception cref="FileNotFoundException">If the file does not exist in the repository.</exception>
        /// <exception cref="XmlException">If the file is not a valid XML document.</exception>
        public static async Task<GlobalJsonFile> ReadAsync(IFileSystem fileSystem, string filePath)
        {
            string content = await fileSystem.ReadAllTextAsync(filePath);
            return new GlobalJsonFile(fileSystem, filePath, content);
        }

        public bool TryRemoveMsBuildSdk(string sdkName)
        {
            if (!Content.Contains("Microsoft.Build.CentralPackageVersions", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Regex regex = new(@$"\s*""Microsoft.Build.CentralPackageVersions"":\s*""\d+\.?\d*\.?\d*"",?",
                RegexOptions.IgnoreCase);
            Content = regex.Replace(Content, string.Empty);
            _document = JsonDocument.Parse(Content, JsonDocumentOptions);
        
            return true;
        }
    }
}
