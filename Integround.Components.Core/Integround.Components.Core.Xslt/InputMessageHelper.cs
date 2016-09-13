using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;

namespace Integround.Components.Core.Xslt
{
    internal class InputMessageHelper
    {
        public static async Task<XPathDocument> CreateXPathDocumentAsync(params Message[] messages)
        {
            XPathDocument xpathDoc;

            if (messages.Length > 1) // If multiple input messages are defined, they must be joined to a multi-part message structure
            {
                using (var stream = new MemoryStream())
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Async = true }))
                {
                    // Write the root element
                    await writer.WriteStartDocumentAsync(true);
                    await writer.WriteStartElementAsync("", Constants.MultipartRootElement, Constants.MultipartNamespace);

                    // Write the contents of each input message:
                    var index = 0;
                    foreach (var msg in messages)
                    {
                        await writer.WriteStartElementAsync("", string.Concat(Constants.MultipartPartElement, index), Constants.MultipartNamespace);

                        // Write the message:
                        using (var reader = XmlReader.Create(msg.ContentStream))
                        {
                            reader.MoveToContent();
                            await writer.WriteNodeAsync(reader, false);
                        }

                        // Rewind the stream:
                        msg.ContentStream.Seek(0, SeekOrigin.Begin);

                        await writer.WriteEndElementAsync();

                        index++;
                    }

                    await writer.WriteEndElementAsync();
                    await writer.WriteEndDocumentAsync();

                    // Flush the writer so the stream can be read:
                    await writer.FlushAsync();

                    // Rewind the stream:
                    stream.Seek(0, SeekOrigin.Begin);

                    using (var reader = XmlReader.Create(stream))
                    {
                        xpathDoc = new XPathDocument(reader);
                    }
                }
            }
            else if (messages.Length == 1)
            {
                var msg = messages.First();

                using (var reader = XmlReader.Create(msg.ContentStream))
                {
                    xpathDoc = new XPathDocument(reader);
                }

                // Rewind the stream:
                msg.ContentStream.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                throw new Exception("No input messages");
            }

            return xpathDoc;
        }
    }
}
