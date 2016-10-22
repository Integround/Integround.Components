using System;
using System.Linq;
using Integround.Components.Core;
using Integround.Components.Log;

namespace Integround.Components.Files
{
    public class FileReceiver
    {
        private readonly IFileClient _client;
        private readonly ILogger _logger;
        private readonly string _path;
        private readonly string _fileMask;
        private bool _running;
        private bool _executing;

        public event EventHandler<ReceiveFileEventArgs> FileReceived;

        public FileReceiver(IFileClient client, IScheduler scheduler, string path, string fileMask, ILogger logger = null)
        {
            _path = path;
            _fileMask = fileMask;
            _client = client;
            _logger = logger;
            scheduler.Trigger += _scheduler_Trigger;
        }

        public void Start()
        {
            _running = true;
        }

        public void Stop()
        {
            _running = false;
        }

        private async void _scheduler_Trigger(object sender, MessageEventArgs e)
        {
            if (!_running || _executing)
                return;

            _executing = true;

            try
            {
                var files = await _client.ReceiveFilesAsync(_path, _fileMask);
                if (files != null)
                {
                    foreach (var file in files.Where(x => !x.IsError))
                    {
                        FileReceived?.Invoke(this, new ReceiveFileEventArgs(file));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Reading files from path '{_path}' using filemask '{_fileMask}' was unsuccessful.", ex);
            }

            _executing = false;
        }
    }
}
