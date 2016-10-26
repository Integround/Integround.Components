using Rebex.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Integround.Components.Core;
using Integround.Components.Log;

namespace Integround.Components.Files.SftpClient
{
    public class SftpClient : IFileClient
    {
        private readonly string _serverAddress;
        private readonly int _port;
        private readonly string _userName;
        private readonly string _password;
        private readonly SftpTransferType _transferType;
        private readonly ILogger _logger;

        public int RetryCount { get; set; }

        public SftpClient(string serverAddress, int port, string userName, string password, bool ascii = false, ILogger logger = null)
        {
            _serverAddress = serverAddress;
            _port = port;
            _userName = userName;
            _password = password;
            _transferType = ascii ? SftpTransferType.Ascii : SftpTransferType.Binary;
            _logger = logger;

            RetryCount = 0;
        }

        public async Task<Message> ReadFileAsync(string path, string fileName)
        {
            Message outputMessage = null;
            var fullPath = Path.Combine(path, fileName).Replace('\\', '/');

            var retries = 0;
            var retryCount = RetryCount;
            while (retries <= retryCount)
            {
                retries++;
                var sleepTime = (int)Math.Pow(retries, retries) * 1000;

                try
                {
                    outputMessage = await ReadAsync(path, fileName);
                    break;
                }
                catch (Exception ex)
                {
                    // If this is not the last retry, log a warning.
                    if (retries <= retryCount)
                    {
                        _logger?.Warning($"Reading the file '{fullPath}' failed. Retrying {retries}/{retryCount} in {sleepTime} ms.");
                    }
                    // Otherwise, throw an exception
                    else if (retryCount == 0)
                        throw new Exception($"Reading the file '{fullPath}' failed.", ex);
                    else
                        throw new Exception($"Reading the file '{fullPath}' failed after {retryCount} retries.", ex);
                }

                // Sleep before retrying:
                await Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(sleepTime);
                });
            }

            return outputMessage;
        }

        public async Task<string> WriteFileAsync(string path, string fileName, Stream messageStream, bool overwriteIfExists = false, bool createDirectory = false)
        {
            var tempFileName = $".{Guid.NewGuid()}.tmp";
            var destinationFilePath = Path.Combine(path, FileNameHelper.PopulateFileNameMacros(fileName)).Replace('\\', '/');
            var tempFilePath = Path.Combine(path, tempFileName).Replace('\\', '/');
            path = path.Replace('\\', '/');

            using (var sftpClient = new Sftp())
            {
                sftpClient.TransferType = _transferType;

                // Connect & authenticate:
                await sftpClient.ConnectAsync(_serverAddress, _port);
                await sftpClient.LoginAsync(_userName, _password);

                // If the directory does not exist, create it if allowed:
                if (!await sftpClient.DirectoryExistsAsync(path))
                {
                    if (createDirectory)
                    {
                        // Examine the path step by step and create directories:
                        StringBuilder directory = null;
                        foreach (var directoryPart in path.Split('/'))
                        {
                            // First directory should not be preceded by '/':
                            if (directory == null)
                            {
                                directory = new StringBuilder();
                                directory.Append(directoryPart);
                            }
                            else
                                directory.AppendFormat("/{0}", directoryPart);

                            // If this directory does not exist, create it and move to the next part:
                            var dirString = directory.ToString();
                            if (!string.IsNullOrWhiteSpace(dirString) && !await sftpClient.DirectoryExistsAsync(dirString))
                                await sftpClient.CreateDirectoryAsync(dirString);
                        }
                    }
                    else
                        throw new Exception($"Directory '{path}' was not found.");
                }

                // Overwrite existing files if allowed:
                if (await sftpClient.FileExistsAsync(destinationFilePath))
                {
                    if (overwriteIfExists)
                        await sftpClient.DeleteFileAsync(destinationFilePath);
                    else
                        throw new Exception($"File '{destinationFilePath}' already exists.");
                }

                // Upload the file with a temporary file name:
                await sftpClient.PutFileAsync(messageStream, tempFilePath);
                await sftpClient.RenameAsync(tempFilePath, destinationFilePath);
                await sftpClient.DisconnectAsync();
            }

            return tempFileName;
        }

        public async Task DeleteFileAsync(string path, string fileName)
        {
            using (var sftpClient = new Sftp())
            {
                sftpClient.TransferType = _transferType;

                await sftpClient.ConnectAsync(_serverAddress, _port);
                await sftpClient.LoginAsync(_userName, _password);
                await sftpClient.DeleteAsync(Path.Combine(path, fileName).Replace('\\', '/'), Rebex.IO.TraversalMode.MatchFilesShallow);
                await sftpClient.DisconnectAsync();
            }
        }

        public async Task MoveFileAsync(string path, string fileName, string newPathAndFileName)
        {
            var retries = 0;
            var retryCount = RetryCount;
            var fullPath = Path.Combine(path, fileName).Replace('\\', '/');

            while (retries <= retryCount)
            {
                retries++;
                var sleepTime = (int)Math.Pow(retries, retries) * 1000;

                try
                {
                    await MoveAsync(path, fileName, newPathAndFileName);
                    break;
                }
                catch (Exception ex)
                {
                    // If this is not the last retry, log a warning.
                    if (retries <= retryCount)
                    {
                        _logger?.Warning($"Moving the file '{fullPath}' failed. Retrying {retries}/{retryCount} in {sleepTime} ms.");
                    }
                    // Otherwise, throw an exception
                    else if (retryCount == 0)
                        throw new Exception($"Moving the file '{fullPath}' failed.", ex);
                    else
                        throw new Exception($"Moving the file '{fullPath}' failed after {retryCount} retries.", ex);
                }

                // Sleep before retrying:
                await Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(sleepTime);
                });
            }
        }

        public async Task<List<ReceiveFileResult>> ReceiveFilesAsync(string path, string fileMask, bool receiveMultiple = true)
        {
            var handledFiles = new List<ReceiveFileResult>();

            using (var client = new Sftp())
            {
                client.TransferType = _transferType;

                await client.ConnectAsync(_serverAddress, _port, SslMode.None);
                await client.LoginAsync(_userName, _password);

                // Get files from the specified path using the given file name mask:
                var items = await client.GetListAsync(path);
                var files = items.GetFiles(fileMask);

                // Ignore files that are already transfering / failed and sort the remaining by filename
                var sortedFiles = files.Where(f => !(f.StartsWith(".") && f.EndsWith(".tmp"))).OrderBy(f => f).ToList();

                if (!receiveMultiple && (sortedFiles.Count > 1))
                {
                    // only process the first file
                    var singleFile = sortedFiles.First();
                    sortedFiles = new List<string> { singleFile };
                }


                // Try to rename the files with temporary names:
                foreach (var file in sortedFiles)
                {
                    var result = new ReceiveFileResult
                    {
                        FileName = file,
                        TempFileName = $".{Guid.NewGuid()}.tmp",
                        Path = path,
                        IsError = false
                    };

                    try
                    {
                        await client.RenameAsync(Path.Combine(path, result.FileName).Replace('\\', '/'), Path.Combine(path, result.TempFileName).Replace('\\', '/'));
                    }
                    catch (Exception ex)
                    {
                        result.IsError = true;
                        result.Message = $"Renaming the file '{result.FileName}' to '{result.TempFileName}' failed: {ex.Message}.";
                    }

                    handledFiles.Add(result);
                }

                await client.DisconnectAsync();
            }

            return handledFiles;
        }
        
        private async Task<Message> ReadAsync(string path, string fileName)
        {
            var stream = new MemoryStream();
            var fullPath = Path.Combine(path, fileName).Replace('\\', '/');

            using (var sftpClient = new Sftp())
            {
                sftpClient.TransferType = _transferType;

                await sftpClient.ConnectAsync(_serverAddress, _port);
                await sftpClient.LoginAsync(_userName, _password);
                await sftpClient.GetFileAsync(fullPath, stream);
                await sftpClient.DisconnectAsync();
            }

            // Rewind the stream:
            stream.Position = 0;

            return new Message { ContentStream = stream };
        }

        private async Task MoveAsync(string path, string fileName, string newPathAndFileName)
        {
            var oldPathAndFileName = Path.Combine(path, fileName).Replace('\\', '/');
            var newFullPath = newPathAndFileName.Replace('\\', '/');

            using (var sftpClient = new Sftp())
            {
                sftpClient.TransferType = _transferType;

                await sftpClient.ConnectAsync(_serverAddress, _port);
                await sftpClient.LoginAsync(_userName, _password);

                var dir = Path.GetDirectoryName(newFullPath)?.Replace('\\', '/');

                // If the directory hasn't been specified, use user home directory
                if (!string.IsNullOrEmpty(dir) && !await sftpClient.DirectoryExistsAsync(dir))
                {
                    // Examine the path step by step and create directories:
                    StringBuilder directory = null;
                    foreach (var directoryPart in dir.Split('/'))
                    {
                        // First directory should not be preceded by '/':
                        if (directory == null)
                        {
                            directory = new StringBuilder();
                            directory.Append(directoryPart);
                        }
                        else
                            directory.AppendFormat("/{0}", directoryPart);

                        // If this directory does not exist, create it and move to the next part:
                        var dirString = directory.ToString();
                        if (!string.IsNullOrWhiteSpace(dirString) && !await sftpClient.DirectoryExistsAsync(dirString))
                            await sftpClient.CreateDirectoryAsync(dirString);
                    }
                }

                if (await sftpClient.FileExistsAsync(newFullPath))
                    await sftpClient.DeleteFileAsync(newFullPath);

                await sftpClient.RenameAsync(oldPathAndFileName, newFullPath);
                await sftpClient.DisconnectAsync();
            }
        }

    }
}
