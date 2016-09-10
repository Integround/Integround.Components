using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Integround.Components.Core;

namespace Integround.Components.Files
{
    public interface IFileClient
    {
        Task<Message> ReadFileAsync(string path, string fileName);
        Task<string> WriteFileAsync(string path, string fileName, Stream messageStream, bool overwriteIfExists = false, bool createDirectory = false);
        Task DeleteFileAsync(string path, string fileName);
        Task MoveFileAsync(string path, string fileName, string newPathAndFileName);
        Task<List<ReceiveFileResult>> ReceiveFilesAsync(string path, string fileMask, bool receiveMultiple = true);
    }
}
