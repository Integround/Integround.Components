using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Integround.Components.Core;
using Integround.Components.Xml.FlatFile.Enums;
using Integround.Components.Xml.FlatFile.Models;

namespace Integround.Components.Xml.FlatFile
{
    public class FlatFileConverter
    {
        public Encoding Encoding { get; set; }
        public Encoding XmlEncoding { get; set; }
        public char DefaultPadChar { get; set; }

        public FlatFileSchemaElement RootElement { get; set; }

        public FlatFileConverter(Stream flatFileSchema)
        {
            if (flatFileSchema == null)
                throw new Exception("Reading the schema failed. Parameter 'stream' is null.");

            Encoding = Encoding.UTF8;
            XmlEncoding = Encoding.UTF8;

            try
            {
                var schema = XmlSchema.Read(flatFileSchema, ValidateEvent);

                // First read the encodings because they are needed to encode static element names:
                var encodingsFound = false;
                foreach (var item in schema.Items)
                {
                    if (!encodingsFound && (item is XmlSchemaAnnotation))
                    {
                        foreach (var annotationItem in ((XmlSchemaAnnotation)item).Items)
                        {
                            if (!encodingsFound && (annotationItem is XmlSchemaAppInfo))
                            {
                                foreach (var markup in ((XmlSchemaAppInfo)annotationItem).Markup)
                                {
                                    if (!encodingsFound && (markup.Attributes != null))
                                    {
                                        if (markup.Attributes["codepage"] != null)
                                        {
                                            Encoding = Encoding.GetEncoding(Convert.ToInt32(markup.Attributes["codepage"].Value));
                                            encodingsFound = true;
                                        }
                                        if (markup.Attributes["codepage_xml"] != null)
                                            Encoding = Encoding.GetEncoding(Convert.ToInt32(markup.Attributes["codepage_xml"].Value));

                                        // Get the default pad character:
                                        DefaultPadChar = ' ';
                                        if ((markup.Attributes["default_pad_char"] != null) &&
                                            (markup.Attributes["pad_char_type"] != null))
                                        {
                                            var padCharString = markup.Attributes["default_pad_char"].Value;

                                            if (markup.Attributes["pad_char_type"].Value == "hex")
                                                DefaultPadChar = (char)Int16.Parse(padCharString.ToLower().Replace("0x", ""), NumberStyles.AllowHexSpecifier);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Then read the elements:
                foreach (var item in schema.Items)
                {
                    if (item is XmlSchemaElement)
                        RootElement = ReadSchema(item as XmlSchemaElement, schema, null);
                }
            }
            catch (Exception)
            {
                throw new FormatException("Reading the schema failed. Invalid file contents.");
            }
        }

        #region FF to XML methods

        public Message ConvertFfToXml(Message input)
        {
            var msg = new Message { ContentStream = new MemoryStream() };
            var outputWriter = new BinaryWriter(msg.ContentStream, XmlEncoding);
            ConvertFfToXml(input.ContentStream, outputWriter);

            // Rewind the stream:
            msg.ContentStream.Position = 0;

            return msg;
        }

        public void ConvertFfToXml(Stream input, Stream output)
        {
            var outputWriter = new BinaryWriter(output, XmlEncoding);
            ConvertFfToXml(input, outputWriter);

            // Rewind the stream:
            output.Position = 0;
        }

        public void ConvertFfToXml(Stream input, BinaryWriter outputWriter)
        {
            ReadElement(RootElement, input, outputWriter, 0);
            outputWriter.Flush();
        }

        private void ReadElement(FlatFileSchemaElement element, Stream stream, BinaryWriter outputWriter, int elementCount)
        {
            // If this is a complex type, get the children recursively:
            if (element.ElementType == SchemaElementType.Record)
            {
                // If the tag name is defined but tag is not found, check if we have already read enough elements:
                if ((element.TagName != null) && !BytesFound(stream, element.TagName))
                {
                    // If the tag name was not found but another element is expected, throw an exception:
                    if (elementCount < element.MinOccurs)
                        throw new Exception($"Unexpected content. Expecting element with tag name '{Encoding.UTF8.GetString(element.TagName)}'.");

                    // Otherwise do nothing and return
                }
                else
                {
                    // If tag name is set, read it:
                    if (element.TagName != null)
                        ReadBytes(element.TagName, stream);

                    // Child elements must written in the correct order. First read the sequence numbers:
                    var elements = element.Children.ToDictionary(child => child.SequenceNumber);

                    using (var childElementStream = new MemoryStream())
                    using (var childElementWriter = new BinaryWriter(childElementStream))
                    using (var attributeStream = new MemoryStream())
                    using (var attributeWriter = new BinaryWriter(attributeStream))
                    {
                        // Read the attributes & child elements one by one:
                        for (var childNumber = 0; childNumber < element.Children.Count; childNumber++)
                        {
                            var count = 0;
                            var childElement = elements[childNumber + 1];
                            var lastChild = (childNumber >= element.Children.Count - 1);

                            // The child element should occur at least MinOccurs times:
                            while ((childElement.MaxOccurs == -1) || (count < childElement.MaxOccurs))
                            {
                                if (element.Structure == FlatFileElementStructure.Delimited)
                                {
                                    bool nextElementFound;

                                    // If the child element has a tag name, include it in the delimiter check:
                                    var delimiterToFind = element.ChildDelimiter;
                                    if (childElement.TagName != null)
                                    {
                                        delimiterToFind = delimiterToFind.Concat(childElement.TagName).ToArray();
                                    }

                                    if (element.ChildOrder == FlatFileElementChildOrder.Prefix)
                                    {
                                        nextElementFound = BytesFound(stream, delimiterToFind);
                                    }
                                    else if (element.ChildOrder == FlatFileElementChildOrder.Infix)
                                    {
                                        // If the parent's child delimiter is found, there is another child element:
                                        nextElementFound = BytesFound(stream, delimiterToFind);

                                        // Else if the parent's child delimiter is not found, there might be another child element:
                                        if (!nextElementFound)
                                        {
                                            // If this is the first child element and it's child is nullable, try to read the value:
                                            if ((count == 0) &&
                                                ((childElement.ValueType == "string") ||
                                                 (childElement.ElementType == SchemaElementType.Record)))
                                                nextElementFound = true;
                                            else
                                                nextElementFound = !BytesFound(stream, childElement.EndDelimiters);
                                        }
                                    }
                                    else // Postfix
                                    {
                                        // If the child element has a tag name, check if it exists next:
                                        if (childElement.TagName != null)
                                            nextElementFound = BytesFound(stream, childElement.TagName);
                                        else
                                        {
                                            // If postfix and parent's child delimiter is not found, there is another element:
                                            nextElementFound = !BytesFound(stream, childElement.EndDelimiters);

                                            // Else if parent's child delimiter is found, try to read nullable values:
                                            if (!nextElementFound)
                                            {
                                                // If the first element was not found and it's optional, read the delimiter and move to the next child:
                                                if ((count == 0) && (childElement.MinOccurs == 0))
                                                {
                                                    ReadBytes(element.ChildDelimiter, stream);
                                                    break;
                                                }

                                                // If this is the first child element, it is not optional and it's child is nullable, try to read the value:
                                                if ((count == 0) && (childElement.MinOccurs > 0) && (childElement.ValueType == "string"))
                                                {
                                                    nextElementFound = true;
                                                }
                                            }
                                        }
                                    }

                                    if (!nextElementFound)
                                    {
                                        if (count < childElement.MinOccurs)
                                            throw new Exception($"Unexpected delimiter detected. Expecting '{childElement.Name}' element.");
                                        break;
                                    }

                                    // If prefix, always read the delimiter first.
                                    // Or if this is a repeating element of a last infix element, read the preceading delimiter first:
                                    if ((element.ChildOrder == FlatFileElementChildOrder.Prefix) ||
                                        ((element.ChildOrder == FlatFileElementChildOrder.Infix) && lastChild && (count > 0)))
                                        ReadBytes(element.ChildDelimiter, stream);

                                    var writer = (childElement.ElementType == SchemaElementType.Attribute)
                                        ? attributeWriter
                                        : childElementWriter;
                                    ReadElement(childElement, stream, writer, count);

                                    // If postfix/infix, read the trailing delimiter.
                                    // Do not try to read a delimiter after the last infix element:
                                    if ((element.ChildOrder == FlatFileElementChildOrder.Postfix) ||
                                        ((element.ChildOrder == FlatFileElementChildOrder.Infix) && !lastChild))
                                        ReadBytes(element.ChildDelimiter, stream);
                                }
                                else
                                {
                                    var writer = (childElement.ElementType == SchemaElementType.Attribute)
                                        ? attributeWriter
                                        : childElementWriter;
                                    ReadElement(childElement, stream, writer, count);
                                }

                                count++;
                            }
                        }

                        attributeWriter.Flush();
                        childElementWriter.Flush();

                        // Construct the elements:

                        // If there is attributes, write them in the starting element.
                        outputWriter.Write(element.StartElement);
                        if (attributeStream.Length > 0)
                        {
                            attributeStream.Position = 0;
                            outputWriter.Write(attributeStream.ToArray());
                        }

                        // If there is child elements, close the starting element, write the children and write the end element.
                        // Otherwise use the empty element closer:
                        if (childElementStream.Length > 0)
                        {
                            childElementStream.Position = 0;

                            outputWriter.Write(element.ElementCloser);
                            outputWriter.Write(childElementStream.ToArray());
                            outputWriter.Write(element.EndElement);
                        }
                        else
                            outputWriter.Write(element.EmptyElementCloser);

                    }
                }
            }
            else // Else read the value:
            {
                if ((element.Parent == null) || (element.Parent.Structure == FlatFileElementStructure.Delimited))
                    ReadValue(element.EndDelimiters, element, stream, outputWriter);
                else
                    ReadPositionalValue(element, stream, outputWriter);
            }
#if DEBUG

            //// NOTE: This does not work when debugging services since azure storage streams do not support Seek().
            //// Works with unit tests though.

            //using (var debugStream = new MemoryStream())
            //{
            //    var position = outputWriter.BaseStream.Position;
            //    outputWriter.BaseStream.Position = 0;
            //    outputWriter.BaseStream.CopyToAsync(debugStream);

            //    outputWriter.BaseStream.Position = position;
            //    debugStream.Position = 0;

            //    System.Diagnostics.Debug.WriteLine(Encoding.UTF8.GetString(debugStream.ToArray()));
            //}
#endif
        }

        private bool BytesFound(Stream stream, byte[] expectedBytes)
        {
            var bytesFound = false;

            // Read forward:
            var bytesRead = 0;
            while (true)
            {
                var readByte = stream.ReadByte();

                if (readByte == -1)
                {
                    // End-of-file byte cannot be rewinded. For that reason, subtract one from the byte count:
                    bytesRead--;
                    break;
                }

                // If the expected bytes were found, exit the loop.
                // If the read byte equals to the last array item, all bytes are found!
                if ((expectedBytes != null) && (bytesRead == expectedBytes.Length - 1) && readByte == expectedBytes[bytesRead])
                {
                    bytesFound = true;
                    break;
                }

                // If the read byte already differ from the expected bytes, 
                // bytes are not found. Exit the loop.
                if ((expectedBytes == null) || (readByte != expectedBytes[bytesRead]))
                    break;

                bytesRead++;
            }

            // Rewind the bytes back to the stream:
            stream.Seek(-1 * (bytesRead + 1), SeekOrigin.Current);

            return bytesFound;
        }

        private bool BytesFound(Stream stream, IEnumerable<byte[]> expectedBytesList)
        {
            var bytesFound = false;

            foreach (var bytes in expectedBytesList)
            {
                // Read forward byte by byte:
                var bytesRead = 0;
                while (true)
                {
                    var readByte = stream.ReadByte();

                    // If end-of-file was detected, exit the loop:
                    if (readByte == -1)
                    {
                        // End-of-file byte cannot be rewinded. For that reason, subtract one from the byte count:
                        bytesRead--;
                        bytesFound = true;
                        break;
                    }

                    // If the bytes were found, exit the loop.
                    // If the read byte equals to the last array item, all bytes are found!
                    if ((bytes != null) && (bytesRead == bytes.Length - 1) && readByte == bytes[bytesRead])
                    {
                        bytesFound = true;
                        break;
                    }

                    // If the read character already differ from the expected bytes,
                    // bytes are not found. Exit the loop.
                    if ((bytes == null) || (readByte != bytes[bytesRead]))
                        break;

                    bytesRead++;
                }

                // Rewind the bytes back to the stream:
                stream.Seek(-1 * (bytesRead + 1), SeekOrigin.Current);

                // If a bytes were found, break the loop:
                if (bytesFound)
                    break;
            }

            return bytesFound;
        }

        private void ReadBytes(byte[] bytes, Stream stream)
        {
            var i = 0;
            using (var readBytes = new MemoryStream())
            {
                while (true)
                {
                    var readByte = stream.ReadByte();
                    readBytes.WriteByte((byte)readByte);

                    if (readByte == -1)
                    {
                        // If the bytes is null, EOF was expected:
                        if (bytes == null)
                            break;

                        throw new Exception("Unexpected end-of-file detected. Expected bytes '" + Encoding.GetString(bytes) + "'.");
                    }

                    // If the bytes were found, exit the loop.
                    // If the read byte equals to the last array item, all bytes are read!
                    if ((i == bytes.Length - 1) && readByte == bytes[i])
                        break;

                    // If the read byte differ from the expected bytes, 
                    // throw exception
                    if (readByte != bytes[i])
                    {
                        // Rewind the stream and throw an error:
                        readBytes.Position = 0;
                        throw new Exception("Bytes not found. Expected '" + Encoding.GetString(bytes) +
                                            "', found '" + Encoding.GetString(readBytes.ToArray()) + "'.");
                    }
                    // Move to the next byte:
                    i++;
                }
            }
        }

        private void ReadValue(IList<byte[]> delimiters, FlatFileSchemaElement element, Stream stream, BinaryWriter outputWriter)
        {
            var valueExists = false;
            var i = 0;

            using (var delimiterStream = new MemoryStream())
            {
                while (true)
                {
                    var readByte = stream.ReadByte();
                    //delimiterStream.WriteByte((byte)readByte);

                    if (readByte == -1)
                    {
                        // If the delimiter is null, EOF was expected:
                        if (delimiters.Contains(null))
                            break;

                        var msg = "Unexpected end-of-file detected. Expected delimiter ";
                        for (var j = 0; j < delimiters.Count; j++)
                        {
                            msg += "'" + Encoding.GetString(delimiters[j]) + "'";
                            if (j < delimiters.Count - 1)
                                msg += " or ";
                        }

                        throw new Exception(msg + ".");
                    }

                    // If the delimiter was found, exit the loop.
                    // If the last byte is equal to the delimiter's last byte, delimiter is found!
                    var readingDelimiter = false;
                    var delimiterFound = false;
                    foreach (var delimiter in delimiters)
                    {
                        if (delimiter == null)
                            continue;

                        if ((i == delimiter.Length - 1) && readByte == delimiter[i])
                        {
                            // Rewind the delimiter back to the stream:
                            stream.Seek(-1 * delimiter.Length, SeekOrigin.Current);
                            delimiterFound = true;
                            break;
                        }

                        // If at least one delimiter still has the same characters, keep on reading the delimiter:
                        if ((i < delimiter.Length) && (readByte == delimiter[i]))
                        {
                            delimiterStream.WriteByte((byte)readByte);
                            readingDelimiter = true;
                        }
                    }

                    // If some delimiter matched the stream, stop reading:
                    if (delimiterFound)
                        break;

                    // If the read character already differs from all expected delimiters, 
                    // write the buffer to the output stream and clear the buffer:
                    if (!readingDelimiter)
                    {
                        // When the first value character is found, write the start element:
                        if (!valueExists)
                        {
                            outputWriter.Write(element.StartElement);

                            // If this is not an attribute, close the start element:
                            if (element.ElementType != SchemaElementType.Attribute)
                                outputWriter.Write(element.ElementCloser);
                        }

                        // If the delimiter was already partially read, write the previously read characters:
                        if (i > 0)
                        {
                            var delimiterString = Encoding.GetString(delimiterStream.ToArray());
                            var escapedDelimiterString = SecurityElement.Escape(delimiterString);
                            if (escapedDelimiterString != null)
                                outputWriter.Write(escapedDelimiterString.ToCharArray());

                            // Clear the delimiter stream:
                            delimiterStream.SetLength(0);
                        }

                        // Write the last read byte:
                        // Escape the XML characters:
                        var escapedChar = SecurityElement.Escape(((char)readByte).ToString(CultureInfo.InvariantCulture));
                        if (escapedChar != null)
                            outputWriter.Write(escapedChar.ToCharArray());

                        // Note that a value exists in the output stream:
                        valueExists = true;

                        // Start comparing the bytes from the start:
                        i = 0;
                        continue;
                    }

                    // Move to the next byte:
                    i++;
                }
            }

            if (element.ElementType == SchemaElementType.Attribute)
            {
                outputWriter.Write(element.ElementCloser);
            }
            else
            {
                if (valueExists)
                    // Write the closing xml element to the output stream:
                    outputWriter.Write(element.EndElement);
                else
                {
                    // Write an empty element:
                    outputWriter.Write(element.StartElement);
                    outputWriter.Write(element.EmptyElementCloser);
                }

            }
        }

        private void ReadPositionalValue(FlatFileSchemaElement element, Stream stream, BinaryWriter outputWriter)
        {
            var readBytes = new byte[element.PosLength];
            var readBytesCount = stream.Read(readBytes, 0, element.PosLength);

            if (readBytesCount != element.PosLength)
                throw new Exception($"Unexpected end-of-file detected. Expected value for '{element.Name}'.");

            var valueString = Encoding.GetString(readBytes);

            // If the value is justified, remove the padding characters:
            if (element.Justification == FlatFileElementJustification.Left)
                valueString = valueString.TrimEnd(DefaultPadChar);
            else if (element.Justification == FlatFileElementJustification.Right)
                valueString = valueString.TrimStart(DefaultPadChar);

            // Construct the attribute or element:
            outputWriter.Write(element.StartElement);
            if (element.ElementType == SchemaElementType.Attribute)
            {
                // Write the value:
                // Escape the XML characters:
                var escapedValue = SecurityElement.Escape(valueString);
                if (escapedValue != null)
                    outputWriter.Write(XmlEncoding.GetBytes(escapedValue));

                // Close the attribute (write '"'):
                outputWriter.Write(element.ElementCloser);
            }
            else
            {
                // Close the start element (write '>'):
                outputWriter.Write(element.ElementCloser);

                // Write the value:
                // Escape the XML characters:
                var escapedValue = SecurityElement.Escape(valueString);
                if (escapedValue != null)
                    outputWriter.Write(XmlEncoding.GetBytes(escapedValue));

                // Write the closing xml element to the output stream:
                outputWriter.Write(element.EndElement);
            }
        }

        #endregion

        #region XML to FF methods

        public Message ConvertXmlToFf(Message input)
        {
            var msg = new Message { ContentStream = new MemoryStream() };
            var outputWriter = new BinaryWriter(msg.ContentStream, XmlEncoding);
            ConvertXmlToFf(input.ContentStream, outputWriter);

            // Rewind the stream:
            msg.ContentStream.Position = 0;

            return msg;
        }

        public void ConvertXmlToFf(Stream input, Stream output)
        {
            var outputWriter = new BinaryWriter(output, Encoding);
            ConvertXmlToFf(input, outputWriter);

            // Rewind the stream:
            output.Position = 0;
        }

        public void ConvertXmlToFf(Stream input, BinaryWriter outputWriter)
        {
            var xmlDoc = new XmlDocument();
            try
            {
                xmlDoc.Load(input);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not read the input XML: " + ex.Message);
            }

            WriteElement(RootElement, xmlDoc, outputWriter, true);
        }

        private void WriteElement(FlatFileSchemaElement element, XmlNode node, BinaryWriter outputWriter, bool lastChild)
        {
            var childOrder = element.Parent?.ChildOrder ?? FlatFileElementChildOrder.Undefined;
            var childDelimiter = element.Parent?.ChildDelimiter;

            // Find the elements/attributes from the xml node:
            List<XmlNode> xmlNodesToWrite;
            if (element.ElementType == SchemaElementType.Attribute)
            {
                var xmlAttributeNodes = new List<XmlNode>();

                var nodes = node.Attributes?.Cast<XmlNode>();
                if (nodes != null)
                    xmlAttributeNodes.AddRange(nodes);

                xmlNodesToWrite = xmlAttributeNodes.Where(n => (n.LocalName == element.Name) && (n.NamespaceURI == (element.Namespace ?? string.Empty))).ToList();
            }
            else
            {
                var xmlChildNodes = new List<XmlNode>(node.ChildNodes.Cast<XmlNode>());
                xmlNodesToWrite = xmlChildNodes.Where(n => (n.LocalName == element.Name) && (n.NamespaceURI == (element.Namespace ?? string.Empty))).ToList();
            }

            // TODO: Check if there is unwanted elements

            if (xmlNodesToWrite.Count < element.MinOccurs)
                throw new Exception($"Invalid input XML. Excepting at least {element.MinOccurs} '{element.Name}' elements.");

            if ((element.MaxOccurs > -1) && (xmlNodesToWrite.Count > element.MaxOccurs))
                throw new Exception($"Invalid input XML. Excepting max {element.MaxOccurs} '{element.Name}' elements.");

            var childDelimiterString = (childDelimiter != null) ? Encoding.UTF8.GetString(childDelimiter) : "\0";

            // Write the found nodes:
            var elementCount = 0;
            foreach (var elementNode in xmlNodesToWrite)
            {
                // If prefix or the last infix element is repeating, write a delimiter:
                if ((childOrder == FlatFileElementChildOrder.Prefix) || (lastChild && (elementCount > 0)))
                    outputWriter.Write(Encoding.GetBytes(childDelimiterString));

                // If the element has a tag name, write it:
                if (element.TagName != null)
                    outputWriter.Write(Encoding.GetBytes(Encoding.UTF8.GetString(element.TagName)));

                // If this a simple element, get the value & write it:
                if ((element.ElementType == SchemaElementType.Attribute) || (element.ElementType == SchemaElementType.Element))
                {
                    var value = "";
                    if (elementNode.NodeType == XmlNodeType.Attribute)
                        value = elementNode.Value;
                    else if ((elementNode.NodeType == XmlNodeType.Element) && elementNode.HasChildNodes)
                        value = elementNode.FirstChild.Value;

                    // If positional, write only certain number of characters:
                    if ((element.Parent != null) && (element.Parent.Structure == FlatFileElementStructure.Positional))
                    {
                        if (element.Justification == FlatFileElementJustification.Left)
                        {
                            while (value.Length < element.PosLength)
                                value += DefaultPadChar;

                            value = value.Substring(0, element.PosLength);
                        }
                        else
                        {
                            while (value.Length < element.PosLength)
                                value = DefaultPadChar + value;

                            value = value.Substring(value.Length - element.PosLength);
                        }
                    }

                    outputWriter.Write(Encoding.GetBytes(value));
                }
                else
                {
                    // Child elements must written in the correct order. First read the sequence numbers:
                    var elements = element.Children.ToDictionary(c => c.SequenceNumber);

                    for (var i = 1; i <= element.Children.Count; i++)
                    {
                        // If the child order is infix and this is the last element, do not write the delimiter:
                        var lastElement = (element.ChildOrder == FlatFileElementChildOrder.Infix) && (i >= element.Children.Count);

                        WriteElement(elements[i], elementNode, outputWriter, lastElement);
                    }
                }

                elementCount++;

                // Skip this if prefix or the last child of infix element:
                if (((childOrder == FlatFileElementChildOrder.Infix) && !lastChild) ||
                    (childOrder == FlatFileElementChildOrder.Postfix))
                    outputWriter.Write(Encoding.GetBytes(childDelimiterString));

            }

            // If the order is postfix and no elements were written, write the delimiter:
            if ((childOrder == FlatFileElementChildOrder.Postfix) &&
                (xmlNodesToWrite.Count == 0))
            {
                outputWriter.Write(Encoding.GetBytes(childDelimiterString));
            }
        }

        #endregion

        #region Private schema methods

        private void ValidateEvent(object o, ValidationEventArgs e)
        {
            // TODO: ?
        }

        private FlatFileSchemaElement ReadSchema(XmlSchemaObject schemaElement, XmlSchema schema, FlatFileSchemaElement parent)
        {
            FlatFileSchemaElement element = null;

            if (schemaElement is XmlSchemaElement)
            {
                var el = (XmlSchemaElement)schemaElement;

                // If parent is null, this is a root element and namespace must be set:
                var ns = (parent == null) ? schema.TargetNamespace : null;

                // Add the namespace if the element is of form 'qualified':
                if ((el.Form == XmlSchemaForm.Qualified) || ((el.Form == XmlSchemaForm.None) && (schema.ElementFormDefault == XmlSchemaForm.Qualified)))
                    ns = schema.TargetNamespace;

                var minOccurs = 1;
                var maxOccurs = 1;
                if (el.MinOccursString == "unbounded")
                    minOccurs = -1;
                else if (el.MinOccursString != null)
                    minOccurs = Convert.ToInt32(el.MinOccursString);

                if (el.MaxOccursString == "unbounded")
                    maxOccurs = -1;
                else if (el.MaxOccursString != null)
                    maxOccurs = Convert.ToInt32(el.MaxOccursString);

                element = new FlatFileSchemaElement(el.Name, ns, minOccurs, maxOccurs, parent, XmlEncoding) { ValueType = el.SchemaTypeName.Name };

                // Read the sequence number:
                foreach (var annotationItem in el.Annotation.Items)
                {
                    var annotation = annotationItem as XmlSchemaAppInfo;

                    var attributes = annotation?.Markup[0].Attributes;
                    if (attributes == null)
                        continue;

                    element.SequenceNumber = Convert.ToInt32(attributes["sequence_number"].Value);
                    if (attributes["justification"] != null)
                    {
                        switch (attributes["justification"].Value)
                        {
                            case "left":
                                element.Justification = FlatFileElementJustification.Left;
                                break;
                            case "right":
                                element.Justification = FlatFileElementJustification.Right;
                                break;
                            default:
                                element.Justification = FlatFileElementJustification.Undefined;
                                break;
                        }
                    }
                    if (attributes["pos_offset"] != null)
                        element.PosOffset = Convert.ToInt32(attributes["pos_offset"].Value);
                    if (attributes["pos_length"] != null)
                        element.PosLength = Convert.ToInt32(attributes["pos_length"].Value);
                }

                // Set the element type:
                if (el.SchemaType is XmlSchemaComplexType)
                {
                    var complexElement = (XmlSchemaComplexType)el.SchemaType;

                    // Get the flat file properties:
                    foreach (var i in el.Annotation.Items)
                    {
                        if (i is XmlSchemaAppInfo)
                        {
                            var annotationItem = i as XmlSchemaAppInfo;

                            if (string.IsNullOrWhiteSpace(element.ValueType))
                            {
                                switch (annotationItem.Markup[0].Attributes["structure"].Value)
                                {
                                    case "delimited":
                                        element.Structure = FlatFileElementStructure.Delimited;
                                        break;
                                    case "positional":
                                        element.Structure = FlatFileElementStructure.Positional;
                                        break;
                                    default:
                                        throw new Exception($"Element {el.Name}: structure not defined.");
                                }

                                if (element.Structure == FlatFileElementStructure.Delimited)
                                {
                                    var childDelimiterType = annotationItem.Markup[0].Attributes["child_delimiter_type"].Value;
                                    var childDelimiter = annotationItem.Markup[0].Attributes["child_delimiter"].Value;

                                    if (childDelimiterType == "hex")
                                    {
                                        var chars = childDelimiter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                                        childDelimiter = "";

                                        foreach (var c in chars)
                                            childDelimiter += (char)short.Parse(c.ToLower().Replace("0x", ""), NumberStyles.AllowHexSpecifier);
                                    }
                                    element.ChildDelimiter = Encoding.UTF8.GetBytes(childDelimiter);

                                    switch (annotationItem.Markup[0].Attributes["child_order"].Value)
                                    {
                                        case "prefix":
                                            element.ChildOrder = FlatFileElementChildOrder.Prefix;
                                            break;
                                        case "infix":
                                            element.ChildOrder = FlatFileElementChildOrder.Infix;
                                            break;
                                        case "postfix":
                                            element.ChildOrder = FlatFileElementChildOrder.Postfix;
                                            break;
                                        default:
                                            throw new Exception($"Element {el.Name}: child order not defined.");
                                    }
                                }

                                if (annotationItem.Markup[0].Attributes["tag_name"] != null)
                                    element.TagName = Encoding.UTF8.GetBytes(annotationItem.Markup[0].Attributes["tag_name"].Value);
                            }
                        }
                    }

                    // Handle the attributes:
                    if (complexElement.Attributes != null)
                        foreach (var attribute in complexElement.Attributes)
                            element.Children.Add(ReadSchema(attribute as XmlSchemaAttribute, schema, element));

                    // Handle the child elements:
                    var particle = complexElement.Particle as XmlSchemaSequence;
                    foreach (var item in particle.Items)
                    {
                        if (item is XmlSchemaElement)
                            element.Children.Add(ReadSchema(item as XmlSchemaElement, schema, element));
                    }

                    element.ElementType = SchemaElementType.Record;
                }
                else
                    element.ElementType = SchemaElementType.Element;

            }
            else if (schemaElement is XmlSchemaAttribute)
            {
                var el = (XmlSchemaAttribute)schemaElement;

                // Add the namespace if the element is of form 'qualified':
                string ns = null;
                if ((el.Form == XmlSchemaForm.Qualified) || ((el.Form == XmlSchemaForm.None) && (schema.ElementFormDefault == XmlSchemaForm.Qualified)))
                    ns = schema.TargetNamespace;

                element = new FlatFileSchemaElement(el.Name, ns, parent, XmlEncoding)
                {
                    ValueType = el.SchemaTypeName.Name
                };

                // Read the sequence number:
                foreach (var annotationItem in el.Annotation.Items)
                {
                    var annotation = annotationItem as XmlSchemaAppInfo;
                    if ((annotation?.Markup != null) && annotation.Markup.Any() && (annotation.Markup[0].Attributes != null))
                        element.SequenceNumber = Convert.ToInt32(annotation.Markup[0].Attributes["sequence_number"].Value);
                }
            }

            return element;
        }

        #endregion
    }
}
