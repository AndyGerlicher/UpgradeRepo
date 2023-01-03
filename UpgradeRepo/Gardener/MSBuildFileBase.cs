// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace Gardener.Core.MSBuild
{
    /// <summary>
    /// This class represents an MSBuild file (a project, props, or targets file) and provides various common operations.
    /// </summary>
    internal abstract class MSBuildFileBase : StructuredFileBase
    {
        // Identifies elements in an xpath
        private static Regex xpathElementRegex = new Regex(@"(^(?<element>[\w]+))|((?<separator>/)(?<element>[\w]+))", RegexOptions.Compiled);

        private XmlDocument? _xmlDocument;

        private XmlNamespaceManager? _xmlNamespaceManager;

        protected MSBuildFileBase(IFileSystem fileSystem, string filePath, string content)
            : base(fileSystem, filePath, content)
        {
            ResetAndValidateXmlDocument();
            PackageReferences = ParsePackageReferences(content);
        }

        public Dictionary<string, HashSet<string>> PackageReferences { get; protected set; }

        /// <summary>
        /// Replaces captured regex pattern.
        /// </summary>
        /// <remarks>
        /// This will return true and reevalute to ensure it is still valid xml if the replacement is successful and false otherwise.
        /// </remarks>
        public bool Replace(string pattern, string replacementValue, RegexOptions regexOptions)
        {
            string oldContent = Content;
            Content = Regex.Replace(Content, pattern, replacementValue, regexOptions);
            if (oldContent != Content)
            {
                ResetAndValidateXmlDocument();
                PackageReferences = ParsePackageReferences(Content);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Replaces the first instance of the captured regex pattern.
        /// </summary>
        /// <remarks>
        /// This will return true and reevalute to ensure it is still valid xml if the replacement is successful and false otherwise.
        /// </remarks>
        public bool ReplaceFirst(string pattern, string replacementValue, RegexOptions regexOptions)
        {
            string oldContent = Content;
            var rgx = new Regex(pattern, regexOptions);
            Content = rgx.Replace(Content, replacementValue, 1);
            if (oldContent != Content)
            {
                ResetAndValidateXmlDocument();
                PackageReferences = ParsePackageReferences(Content);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try find a regular expression pattern in the content of thw file.
        /// </summary>
        /// <param name="pattern">The RegExp pattern to look for. Make sure to incluyde at least one group for the the output result.</param>
        /// <param name="resultGroupIndex">The regexp match gropup index to local teh group matched -- if any is found.</param>
        /// <param name="regexOptions">Options to pass to the RegExp match.</param>
        /// <param name="result">The value of the group match found by regexp.</param>
        /// <returns>True if matching was succesful; false otherwise.</returns>
        public bool TryFindRegExp(string pattern, int resultGroupIndex, RegexOptions regexOptions, out string result)
        {
            var match = Regex.Match(Content, pattern, regexOptions);
            if (match.Success)
            {
                result = match.Groups[resultGroupIndex].Value;
                return true;
            }

            result = string.Empty;
            return false;
        }

        protected XmlNodeList SelectNodes(string xpath)
        {
            if (_xmlNamespaceManager != null)
            {
                return _xmlDocument!.SelectNodes(
                    xpathElementRegex.Replace(xpath, "${separator}msbuild:${element}"),
                    _xmlNamespaceManager);
            }

            return _xmlDocument!.SelectNodes(xpath);
        }

        protected abstract Dictionary<string, HashSet<string>> ParsePackageReferences(string contents);

        protected void ResetAndValidateXmlDocument()
        {
            var xmlReaderSettings = new XmlReaderSettings
            {
                XmlResolver = null,
            };
            using var stringReader = new StringReader(Content);
            using var xmlReader = XmlReader.Create(stringReader, xmlReaderSettings);
            var xmlDocument = new XmlDocument
            {
                XmlResolver = null,
                PreserveWhitespace = true,
            };

            // This may throw an XmlException if the content is invalid.
            xmlDocument.Load(xmlReader);

            if (!string.IsNullOrEmpty(xmlDocument.DocumentElement.NamespaceURI))
            {
                _xmlNamespaceManager = new XmlNamespaceManager(xmlDocument.NameTable);
                _xmlNamespaceManager.AddNamespace("msbuild", xmlDocument.DocumentElement.NamespaceURI);
            }

            _xmlDocument = xmlDocument;
        }
    }
}
