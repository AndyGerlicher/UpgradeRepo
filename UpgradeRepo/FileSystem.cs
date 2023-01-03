using System;
using System.Collections.Generic;
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
    }
}
