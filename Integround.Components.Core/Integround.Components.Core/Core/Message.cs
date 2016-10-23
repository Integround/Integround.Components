using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Integround.Components.Core
{
    public class Message : IDisposable
    {
        public Dictionary<string, string> Metadata { get; set; }
        [Obsolete]
        public ConcurrentDictionary<string, string> Properties { get; set; }
        public Stream ContentStream { get; set; }
        
        public Message()
        {
            Metadata = new Dictionary<string, string>();
        }
        public Message(Stream stream) : this()
        {
            ContentStream = stream;
        }

        public static async Task<Message> CreateFromStringAsync(string str)
        {
            var msg = new Message { ContentStream = new MemoryStream() };

            using (var writer = new StreamWriter(msg.ContentStream, Encoding.UTF8, 4096, true))
            {
                await writer.WriteAsync(str);
                await writer.FlushAsync();
            }

            // Rewind the stream:
            msg.ContentStream.Position = 0;

            return msg;
        }

        public static Message CreateFromObject<T>(T obj)
        {
            var msg = new Message { ContentStream = new MemoryStream() };
            msg.Metadata["Type"] = typeof(T).ToString();

            // Serialize the request:
            var serializer = new XmlSerializer(typeof(T));
            serializer.Serialize(msg.ContentStream, obj);

            // Rewind the stream:
            msg.ContentStream.Position = 0;

            return msg;
        }

        public static T ExtractObject<T>(Message msg)
        {
            // Deserialize the message contents:
            var serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(msg.ContentStream);
        }

        public void Dispose()
        {
            ContentStream?.Dispose();
            ContentStream = null;
        }
    }
}
