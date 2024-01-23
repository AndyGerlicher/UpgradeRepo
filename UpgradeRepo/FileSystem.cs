using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gardener.Core;

namespace UpgradeRepo
{
    internal class FileSystem : IFileSystem
    {
        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(File.Exists(path));
        }

        public Task<string> ReadAllTextAsync(string path)
        {
            return File.ReadAllTextAsync(path);
        }

        public Task<byte[]> ReadAllBytesAsync(string path)
        {
            return File.ReadAllBytesAsync(path);
        }

        public Task WriteAllTextAsync(string path, string content)
        {
            return File.WriteAllTextAsync(path, content);
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes)
        {
            return File.WriteAllBytesAsync(path, bytes);
        }

        public Task DeleteFileAsync(string path)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        public Task RenameFileAsync(string path, string newName)
        {
            File.Move(path, newName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<string>> EnumerateFiles(IReadOnlyCollection<string> searchPatterns)
        {
            var items = Directory.GetFiles(Environment.CurrentDirectory, "*.*", SearchOption.AllDirectories);
            var result = new List<string>();
            static string ConvertSearchPatternToRegex(string searchPattern)
            {
                string regex = searchPattern.Replace(".", "\\.").Replace("*", ".*").Replace("?", ".");
                return $"^{regex}$";
            }

            var regex = new Regex(ConvertSearchPatternToRegex(string.Join("|", searchPatterns)), RegexOptions.Compiled);
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (regex.IsMatch(item))
                    {
                        result.Add(item);
                    }
                }
            }

            return Task.FromResult((IReadOnlyCollection<string>)result.AsReadOnly());
        }

        public Task<IEnumerable<string>> GetFilesAsync(string path, string searchPattern)
        {
            return Task.FromResult(Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories).AsEnumerable());
        }

        public Task<string[]> ReadAllLinesAsync(string path, Encoding encoding)
        {
            return File.ReadAllLinesAsync(path, encoding);
        }
    }
}
