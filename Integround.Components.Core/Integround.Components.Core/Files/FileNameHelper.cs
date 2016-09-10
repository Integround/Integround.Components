using System;

namespace Integround.Components.Files
{
    public class FileNameHelper
    {
        public static string PopulateFileNameMacros(string fileName)
        {
            // If the file name is not defined, guid is used:
            if (string.IsNullOrEmpty(fileName))
                fileName = "%NewGuid%";

            // Replace the macros with corresponding strings:
            fileName = fileName.Replace(@"%NewGuid%", Guid.NewGuid().ToString("D"));
            fileName = fileName.Replace(@"%TimestampUtc%", DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmss"));

            return fileName;
        }
    }
}
