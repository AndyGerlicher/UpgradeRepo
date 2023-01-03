// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Gardener.Core.MSBuild
{
    /// <summary>
    /// This class represents an MSBuild file (a project, props, or targets file) and provides various common operations.
    /// </summary>
    internal class MSBuildFile : MSBuildFileBase
    {
        private const string ProjectElementEndTag = "</Project>";
        private const string DefaultPropertyGroupIndentation = "  ";
        private const string DefaultPropertyIndentation = "    ";

        public readonly struct Property
        {
            public Property(string name, string value, string? condition = null)
            {
                Name = name;
                Value = value;
                Condition = condition;
            }

            public string XML => @$"<{Name}{(Condition is null ? string.Empty : $@" Condition=""{Condition}""")}>{Value}</{Name}>";

            public readonly string Name;
            public readonly string Value;
            public readonly string? Condition;
        }

        internal string PropertyGroupIndentation { get; }

        internal string PropertyIndentation { get; }

        protected MSBuildFile(IFileSystem fileSystem, string filePath, string content)
            : base(fileSystem, filePath, content)
        {
            PropertyGroupIndentation =
                TryFindRegExp(
                    @"^([^\S\r\n]*)<PropertyGroup.*?>",
                    1,
                    RegexOptions.Multiline,
                    out string propGroupIndent)
                ? propGroupIndent : DefaultPropertyGroupIndentation;

            PropertyIndentation =
                TryFindRegExp(
                    @"<PropertyGroup.*?>\s*\r?\n([^\S\r\n]*)<",
                    1,
                    RegexOptions.Compiled,
                    out string propIndent)
                ? propIndent : DefaultPropertyIndentation;
        }

        /// <summary>
        /// Reads an <see cref="MSBuildFile"/> from the provided file path.
        /// </summary>
        /// <exception cref="FileNotFoundException">If the file does not exist in the repository.</exception>
        /// <exception cref="XmlException">If the file is not a valid XML document.</exception>
        public static async Task<MSBuildFile> ReadAsync(IFileSystem fileSystem, string filePath)
        {
            if (!await fileSystem.FileExistsAsync(filePath))
            {
                throw new FileNotFoundException("File not found", filePath);
            }

            string content = await fileSystem.ReadAllTextAsync(filePath);

            return new MSBuildFile(fileSystem, filePath, content);
        }

        /// <summary>
        /// Determines whether a property is set in this MSBuild file.
        /// </summary>
        /// <remarks>
        /// No conditions are evaluated, so this will return true even if the condition could never be met.
        /// </remarks>
        public bool IsPropertySet(string propertyName)
            => SelectNodes($"Project/PropertyGroup/{propertyName}").Count > 0;

        /// <summary>
        /// Takes a dictionary of properties, generates their XML representation and inserts
        /// them into the document.
        /// If a property is already present in the document this function will modify
        /// its value instead of creating a new one.
        /// Either creates a new PropertyGroup to contain all newly-added properties
        /// OR adds them to an existing PropertyGroup depending on the value of 'label'.
        /// </summary>
        /// <param name="props">
        /// A dictionary with properties as keys and their values as dictionary values.
        /// The key can also contain optional property attributes such as
        /// 'PropertyName Label="SomeLabel"'.
        /// </param>
        /// <param name="label">
        /// An optional PropertyGroup label.  If provided, SetProperties will first search
        /// for an existing PropertyGroup with that label and add all new properties to it.
        /// If no existing matching label is found, a new PropertyGroup with that label will
        /// be created to contain any new properties.
        /// </param>
        public void SetProperties(IEnumerable<Property> props, string? label = null)
        {
            // Update values of existing properties
            props
                .Where(prop => IsPropertySet(prop.Name))
                .ForEach(prop => Replace(
                    $"<{prop.Name}.*?<\\/{prop.Name}>",
                    prop.XML,
                    RegexOptions.Singleline));

            var newProperties = props.Where(prop => !IsPropertySet(prop.Name));
            if (newProperties.Any())
            {
                string rgx;
                if (!string.IsNullOrEmpty(label))
                {
                    // Insert properties into existing PropertyGroup with specified label.
                    rgx = @$"(<PropertyGroup\s+Label=""{label}"">.*?)(\s*<\/PropertyGroup>)";
                }
                else
                {
                    // Insert properties into the first unlabeled PropertyGroup.
                    rgx = @$"(<PropertyGroup(?:(?!:Label=).)*?>.*?)(\s*<\/PropertyGroup>)";
                }

                var propsToInsert = string.Join(
                        LineEnding,
                        newProperties.Select(prop => $"{PropertyIndentation}{prop.XML}"));

                if (!ReplaceFirst(rgx, $"$1{LineEnding}{propsToInsert}$2", RegexOptions.Singleline))
                {
                    // Create new property group for missing properties and insert.
                    InsertPropertyGroup(newProperties, label: label);
                }
            }
        }

        /// <summary>
        /// Takes a dictionary of properties, generates their XML representation and inserts
        /// a new PropertyGroup into the document containing all of the properties.
        /// </summary>
        /// <param name="props">
        /// A dictionary with properties as keys and their values as dictionary values.
        /// The key can also contain optional property attributes such as
        /// 'PropertyName Label="SomeLabel"'.
        /// </param>
        /// <param name="label">
        /// An optional label to append to the newly created PropertyGroup.
        /// </param>
        public void InsertPropertyGroup(IEnumerable<Property> props, string? label = null) =>
            AppendTopLevelXml(
                string.Join(
                    LineEnding,
                    $"{PropertyGroupIndentation}<PropertyGroup{(label is not null ? @$" Label=""{label}""" : string.Empty)}>",
                    string.Join(
                        LineEnding,
                        props.Select(prop => $"{PropertyIndentation}{prop.XML}")),
                    $"{PropertyGroupIndentation}</PropertyGroup>"));

        /// <summary>
        /// Removes property with specified name from the xml document.
        /// </summary>
        public void RemoveProperty(string propertyName) =>
            Replace(
                @$"[\s]*<{propertyName}.*?<\/{propertyName}>",
                string.Empty,
                RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Appends top-level content to the MSBuild file. This includes a new PropertyGroup, ItemGroup, or Import.
        /// </summary>
        /// <remarks>
        /// The provided content is expected to be valid xml.
        /// </remarks>
        public void AppendTopLevelXml(string newContent)
        {
            if (string.IsNullOrEmpty(newContent))
            {
                throw new ArgumentException("A value must be provided", nameof(newContent));
            }

            // Format the new content properly.
            newContent = NormalizeLineEndings(newContent);

            int indexToInsert = Content.IndexOf(ProjectElementEndTag, StringComparison.OrdinalIgnoreCase);

            // Determine whether there is already a double newline before the project end element.
            // If so, we don't need to prepend our own.
            bool shouldPrependNewline = !IndexFollowsDoubleNewline(Content, indexToInsert);

            if (shouldPrependNewline)
            {
                newContent = LineEnding + newContent;
            }

            Content = Content.Insert(indexToInsert, newContent);
            ResetAndValidateXmlDocument();
        }

        /// <summary>
        /// Removes top-level content from the MSBuild file. Given the same inputs, will reverse
        /// a change produced by <see cref="AppendTopLevelXml"/>.
        /// </summary>
        /// <remarks>
        /// If the provided content is not found, this no-ops.
        /// </remarks>
        public void RemoveTopLevelXml(string contentToRemove)
        {
            if (string.IsNullOrEmpty(contentToRemove))
            {
                throw new ArgumentException("A value must be provided", nameof(contentToRemove));
            }

            // Format the content and source file to match before searching to account for mismatched line endings.
            contentToRemove = NormalizeLineEndings(contentToRemove);
            string normalizedContent = Content.WithLineEndings(LineEnding);

            int indexToRemove = normalizedContent.IndexOf(contentToRemove, StringComparison.OrdinalIgnoreCase);

            if (indexToRemove == -1)
            {
                return;
            }

            // Determine whether there is already a double newline before the content we want to remove.
            // If so, we need to also remove it.
            if (IndexFollowsDoubleNewline(normalizedContent, indexToRemove))
            {
                contentToRemove = LineEnding + contentToRemove;
            }

            Content = normalizedContent.Replace(contentToRemove, string.Empty);
            ResetAndValidateXmlDocument();
        }

        private bool IndexFollowsDoubleNewline(string content, int contentIndex)
        {
            string doubleNewLine = LineEnding + LineEnding;
            string possibleDoubleNewLine = content.Substring(contentIndex - doubleNewLine.Length, doubleNewLine.Length);

            return possibleDoubleNewLine.Equals(doubleNewLine, StringComparison.Ordinal);
        }

        private string NormalizeLineEndings(string content)
        {
            return content
               .WithLineEndings(LineEnding)
               .Trim(LineEnding.ToCharArray())
               + LineEnding;
        }

        /// <summary>
        /// Parses all PackageReference nodes in the content of an <see cref="MSBuildFile"/> and updates its PackageReferences dictionary.
        /// </summary>
        /// <remarks>
        /// The provided content is expected to be valid xml.
        /// </remarks>
        protected override Dictionary<string, HashSet<string>> ParsePackageReferences(string contents)
        {
            var packagesMapping = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var doc = XDocument.Load(new StringReader(contents));
            var packages = doc.Root.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "ItemGroup", StringComparison.OrdinalIgnoreCase))
                .Elements()
                .Where(e =>
                    (string.Equals(e.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.Name.LocalName, "GlobalPackageReference", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.Name.LocalName, "PackageVersion", StringComparison.OrdinalIgnoreCase))
                    && e.HasAttributes
                    && (e.Attribute("Include") != null || e.Attribute("Update") != null));
            foreach (var package in packages)
            {
                var id = package.Attribute("Include")?.Value;
                if (id is null)
                {
                    id = package.Attribute("Update")?.Value;
                }

                if (id is null)
                {
                    throw new InvalidOperationException($"Unable to determine package id of (Global)PackageReference: {package}");
                }

                var version = package.Attribute("Version");
                if (version is null)
                {
                    version = package.Attribute("VersionOverride");
                }

                if (packagesMapping.ContainsKey(id) && version is not null)
                {
                    packagesMapping[id].Add(version.Value);
                }
                else
                {
                    packagesMapping.Add(
                        id,
                        (version is null) ? new HashSet<string> { } : new HashSet<string> { version.Value });
                }
            }

            return packagesMapping;
        }
    }
}
