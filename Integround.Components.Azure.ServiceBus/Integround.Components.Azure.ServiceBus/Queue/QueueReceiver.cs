using System;
using System.Collections.Concurrent;
using System.IO;
using Integround.Components.Core;
using Microsoft.ServiceBus.Messaging;

namespace Integround.Components.Azure.ServiceBus.Queue
{
    public class QueueReceiver : IScheduler
    {
        public event EventHandler<MessageEventArgs> Trigger;

        private readonly string _path;
        private readonly string _connectionString;
        private Microsoft.ServiceBus.Messaging.QueueClient _queueClient;

        public QueueReceiver(string connectionString, string path)
        {
            _connectionString = connectionString;
            _path = path;
        }

        public void Start()
        {
            if (_queueClient != null)
                throw new Exception("Receiver is already started.");

            _queueClient = Microsoft.ServiceBus.Messaging.QueueClient.CreateFromConnectionString(_connectionString, _path, ReceiveMode.ReceiveAndDelete);
            _queueClient.OnMessageAsync(async message =>
            {
                var msg = new Message
                {
                    Properties = new ConcurrentDictionary<string, string> {["Path"] = _path }
                };


                using (var msgStream = message.GetBody<Stream>())
                {
                    if (msgStream != null)
                    {
                        msg.ContentStream = new MemoryStream();
                        await msgStream.CopyToAsync(msg.ContentStream);
                        
                        // Rewind the stream:
                        msg.ContentStream.Position = 0;
                    }
                }

                // Raise the event if subscribers exist:
                Trigger?.Invoke(this, new MessageEventArgs(msg));
            });
        }

        public void Stop()
        {
            if ((_queueClient != null) && !_queueClient.IsClosed)
                _queueClient.Close();

            _queueClient = null;
        }
    }
}
