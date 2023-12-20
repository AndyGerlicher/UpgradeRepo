using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            var files = new List<string>();
            foreach (var pattern in searchPatterns)
            {
                files.AddRange(Directory.GetFiles(Environment.CurrentDirectory, pattern, SearchOption.AllDirectories));
            }
            return Task.FromResult((IReadOnlyCollection<string>)files.AsReadOnly());
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
