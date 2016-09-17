using Microsoft.ServiceBus.Messaging;
using System.Threading.Tasks;
using Integround.Components.Core;

namespace Integround.Components.Azure.ServiceBus.Queue
{
    public class QueueClient
    {
        private readonly string _connectionString;

        public QueueClient(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task SendAsync(string path, Message msg)
        {
            var queueClient = Microsoft.ServiceBus.Messaging.QueueClient.CreateFromConnectionString(_connectionString, path, ReceiveMode.ReceiveAndDelete);

            await queueClient.SendAsync(new BrokeredMessage(msg.ContentStream, false));
        }
    }
}
