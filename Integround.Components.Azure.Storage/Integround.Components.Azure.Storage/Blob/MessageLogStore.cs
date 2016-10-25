using Integround.Components.Core;
using Integround.Components.Files;
using Integround.Components.Log;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Integround.Components.Azure.Storage.Blob
{
    public class MessageLogStore : IMessageLogStore
    {
        public const string FileNameFormat = "%Date%/%Time%_%NewGuid%";
        private const int MaxMessageAgeInDays = 14;

        private readonly CloudBlobClient _blobClient;
        private readonly string _path;
        private readonly ILogger _log;
        private bool _executing;

        public LoggingLevel LoggingLevel { get; }

        public MessageLogStore(string connectionString, string path,
            LoggingLevel level = LoggingLevel.Info,
            IScheduler scheduler = null,
            ILogger log = null)
            : this(default(CloudBlobClient), path, level, scheduler, log)
        {
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            _blobClient = storageAccount.CreateCloudBlobClient();
        }

        /// <summary>
        /// This constructor is for unit testing purposes only
        /// </summary>
        protected MessageLogStore(CloudBlobClient blobClient, string path,
            LoggingLevel level = LoggingLevel.Info,
            IScheduler scheduler = null,
            ILogger log = null)
        {
            _path = path;
            _log = log;
            _blobClient = blobClient;
            LoggingLevel = level;
            
            if (scheduler != null)
            {
                scheduler.Trigger += Scheduler_Trigger;
            }
        }

        private async void Scheduler_Trigger(object sender, MessageEventArgs e)
        {
            if (_executing)
                return;

            _executing = true;
            
            await CleanUp();

            _executing = false;
        }

        public async Task<string> AddAsync<T>(T obj)
        {
            return await AddAsync(obj, null);
        }

        public async Task<string> AddAsync<T>(T obj, string fileName)
        {
            // TODO: Implement retry
            
            var blobName = FileNameHelper.PopulateFileNameMacros(!string.IsNullOrWhiteSpace(fileName) ? fileName : FileNameFormat);

            try
            {
                var container = _blobClient.GetContainerReference(_path);
                await container.CreateIfNotExistsAsync();

                var blob = container.GetBlockBlobReference(blobName);

                var message = obj as Message;
                if (message != null)
                {
                    // Save the message contents:
                    if (message.ContentStream != null)
                    {
                        var position = message.ContentStream.Position;

                        message.ContentStream.Position = 0;
                        await blob.UploadFromStreamAsync(message.ContentStream);

                        // Restore the position:
                        message.ContentStream.Position = position;
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
                _log.Error($"Adding object to the message log store failed (path '{_path}', filename '{blobName}')", ex);
            }

            return blobName;
        }

        public async Task<string> AddDebugAsync<T>(T obj)
        {
            return await AddDebugAsync(obj, null);
        }

        public async Task<string> AddDebugAsync<T>(T obj, string fileName)
        {
            if (LoggingLevel > LoggingLevel.Debug)
                return null;

            return await AddAsync(obj, fileName);
        }

        public async Task<int> CleanUp(int maxMessageAgeInDays = MaxMessageAgeInDays)
        {
            var container = _blobClient.GetContainerReference(_path);
            if (!await container.ExistsAsync())
                return 0;

            var blobs = container.ListBlobs();
            var tasks = new List<Task>();
            foreach (var blob in blobs.OfType<CloudBlobDirectory>())
            {
                var prefix = (blob.Prefix ?? string.Empty).Split('/').First();

                DateTime date;
                if (!DateTime.TryParseExact(prefix, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out date))
                    continue;

                // Check if the directory is too old:
                if (date.AddDays(maxMessageAgeInDays) >= DateTime.UtcNow.Date)
                    continue;

                // Start removing the blobs:
                var oldBlobs = container.ListBlobs(prefix, true).ToArray();
                tasks.AddRange(oldBlobs.Select(x => ((CloudBlockBlob)x).DeleteIfExistsAsync()));

                _log?.Debug($"Deleting {oldBlobs.Length} messages from '{_path}/{prefix}'.");
            }

            // Wait until blobs are deleted:
            await Task.WhenAll(tasks);

            _log?.Debug($"Finished deleting {tasks.Count} messages from '{_path}'.");

            return tasks.Count;
        }
    }
}
