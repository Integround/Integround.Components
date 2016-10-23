using Integround.Components.Core;
using Integround.Components.Files;
using Integround.Components.Log;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Serialization;

namespace Integround.Components.Azure.Storage.Blob
{
    public class MessageLogStore : IMessageLogStore
    {
        private const string FileNameFormat = "%Date%/%Time%_%NewGuid%";
        private const int RetentionTimeInDays = 14;
        private const int CleanupIntervalInMinutesDefault = 24 * 60;

        private readonly CloudBlobClient _blobClient;
        private readonly string _path;
        private readonly string _fileNameFormat;
        private readonly int _retentionTimeInDays;
        private readonly ILogger _log;
        private readonly Timer _timer;

        public int CleanupIntervalInMinutes { get; set; }

        public LoggingLevel LoggingLevel { get; }

        public MessageLogStore(string connectionString, string path,
            string fileNameFormat = FileNameFormat,
            int retentionTimeInDays = RetentionTimeInDays,
            LoggingLevel level = LoggingLevel.Info,
            ILogger log = null)
            : this(null, path, fileNameFormat, retentionTimeInDays, CleanupIntervalInMinutesDefault, level, log)
        {
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            _blobClient = storageAccount.CreateCloudBlobClient();
        }

        /// <summary>
        /// This constructor is for unit testing purposes only
        /// </summary>
        protected MessageLogStore(CloudBlobClient blobClient, string path,
            string fileNameFormat = FileNameFormat,
            int retentionTimeInDays = 0,
            int cleanupIntervalInMinutes = CleanupIntervalInMinutesDefault,
            LoggingLevel level = LoggingLevel.Info,
            ILogger log = null)
        {
            _path = path;
            _fileNameFormat = fileNameFormat;
            _retentionTimeInDays = retentionTimeInDays;
            _log = log;
            _blobClient = blobClient;

            LoggingLevel = level;

            // TODO: Implement a better way for clean-up

            // If there is a retention time defined, start the timer for clean up:
            if (retentionTimeInDays > 0)
            {
                CleanupIntervalInMinutes = cleanupIntervalInMinutes;

                _timer = new Timer(CleanupIntervalInMinutes * 60 * 1000);
                _timer.Elapsed += _timer_Elapsed;
                _timer.Start();
            }
        }

        public async Task<string> AddAsync<T>(T obj)
        {
            // TODO: Implement retry

            if (_retentionTimeInDays <= 0)
                return null;

            var fileName = FileNameHelper.PopulateFileNameMacros(_fileNameFormat);

            try
            {
                var container = _blobClient.GetContainerReference(_path);
                await container.CreateIfNotExistsAsync();

                var blob = container.GetBlockBlobReference(fileName);

                var message = obj as Message;
                if (message != null)
                {
                    // Save the message contents:
                    if (message.ContentStream != null)
                    {
                        await blob.UploadFromStreamAsync(message.ContentStream);

                        // Rewind the stream:
                        message.ContentStream.Position = 0;
                    }
                    else
                    {
                        await blob.UploadTextAsync(string.Empty);
                    }

                    // Save the properties as blob metadata:
                    if (message.Metadata?.Any() ?? false)
                    {
                        foreach (var property in message.Metadata)
                            blob.Metadata[property.Key] = property.Value;
                        await blob.SetMetadataAsync();
                    }
                }
                else
                {
                    // Serialize the object to the blob stream:
                    using (var stream = await blob.OpenWriteAsync())
                    {
                        var serializer = new XmlSerializer(typeof(T));
                        serializer.Serialize(stream, obj);
                    }

                    // Save the type:
                    blob.Metadata["Type"] = typeof(T).ToString();
                    await blob.SetMetadataAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Adding object to the message log store failed (path '{_path}', filename '{fileName}')", ex);
            }

            return fileName;
        }
        
        public async Task<string> AddDebugAsync<T>(T obj)
        {
            if (LoggingLevel > LoggingLevel.Debug)
                return null;

            return await AddAsync(obj);
        }

        public async Task DeleteOldMessagesAsync()
        {
            var container = _blobClient.GetContainerReference(_path);
            if (!await container.ExistsAsync())
                return;

            var blobs = container.ListBlobs();
            foreach (var blob in blobs.OfType<CloudBlobDirectory>())
            {
                var prefix = (blob.Prefix ?? string.Empty).Split('/').First();

                DateTime date;
                if (!DateTime.TryParseExact(prefix, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out date))
                    continue;

                // Check if the directory is too old:
                if (date.AddDays(_retentionTimeInDays) >= DateTime.UtcNow.Date)
                    continue;

                // Start removing the blobs:
                var tasks = new List<Task>();
                var oldBlobs = container.ListBlobs(prefix, true).ToArray();
                _log?.Debug($"Start deleting {oldBlobs.Length} messages with the prefix '{prefix}'.");

                foreach (var oldBlob in oldBlobs)
                {
                    // Otherwise delete the directory blob, it's too old:   
                    tasks.Add(((CloudBlockBlob)oldBlob).DeleteIfExistsAsync());
                }
                await Task.WhenAll(tasks);

                _log?.Debug($"Finished deleting {tasks.Count} messages with the prefix '{prefix}'.");
            }
        }

        private async void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _timer.Stop();
            try
            {
                await DeleteOldMessagesAsync();
            }
            catch (Exception ex)
            {
                _log?.Error("Integround.Components.Azure.Storage.MessageLogBlobStorage: Deleting old messages failed.", ex);
            }
            _timer.Interval = CleanupIntervalInMinutes;
            _timer.Start();
        }
    }
}
