﻿// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System.Threading.Tasks;

namespace Gardener.Core
{
    public interface IFileSystem
    {
        Task<bool> FileExistsAsync(string path);

        Task<string> ReadAllTextAsync(string path);

        Task<byte[]> ReadAllBytesAsync(string path);

        Task WriteAllTextAsync(string path, string content);

        Task WriteAllBytesAsync(string path, byte[] bytes);

        Task DeleteFileAsync(string path);

        Task RenameFileAsync(string path, string newName);
    }
}
