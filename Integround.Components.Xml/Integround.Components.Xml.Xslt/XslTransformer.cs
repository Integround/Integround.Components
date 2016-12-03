using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using Integround.Components.Core;

namespace Integround.Components.Xml.Xslt
{
    public class XslTransformer
    {
        private readonly XslCompiledTransform _xslTransform;

        public XslTransformer(Stream xsltStream)
        {
            _xslTransform = new XslCompiledTransform();

            using (var reader = XmlReader.Create(xsltStream))
            {
                _xslTransform.Load(reader, new XsltSettings(true, true), null);
            }
        }

        public async Task<Message[]> TransformAsync(params Message[] inputMessages)
        {
            var inputXpathDoc = await InputMessageHelper.CreateXPathDocumentAsync(inputMessages);
            var outputMessages = new List<Message>();
            
            // Execute the transform:
            using (var outputStream = new MemoryStream())
            {
                _xslTransform.Transform(inputXpathDoc, null, outputStream);

                // Rewind the stream
                outputStream.Position = 0;

                using (var outputReader = XmlReader.Create(outputStream))
                {
                    // Load the message:
                    var outputMessageNavigator = new XPathDocument(outputReader).CreateNavigator();

                    outputMessageNavigator.MoveToFirstChild();

                    // If this is a multi-part output message, create a new message for each part:
                    if (string.Equals(outputMessageNavigator.LocalName, Constants.MultipartRootElement) &&
                        string.Equals(outputMessageNavigator.NamespaceURI, Constants.MultipartNamespace))
                    {
                        var messageParts = outputMessageNavigator.Select(string.Format("/*[local-name()='{0}' and namespace-uri()='{2}']/*[starts-with(local-name(), '{1}') and namespace-uri()='{2}']",
                            Constants.MultipartRootElement,
                            Constants.MultipartPartElement,
                            Constants.MultipartNamespace));

                        foreach (XPathNavigator part in messageParts)
                        {
                            var msg = new Message { ContentStream = new MemoryStream(Encoding.UTF8.GetBytes(part.InnerXml)) };
                            outputMessages.Add(msg);
                        }
                    }
                    else
                    {
                        // Rewind the stream
                        outputStream.Position = 0;

                        // Copy the result to an output message:
                        var msg = new Message { ContentStream = new MemoryStream() };
                        await outputStream.CopyToAsync(msg.ContentStream);

                        // Rewind the stream
                        msg.ContentStream.Position = 0;

                        outputMessages.Add(msg);
                    }
                }
            }

            return outputMessages.ToArray();
        }
    }
}
