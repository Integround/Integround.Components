using System.Collections.Generic;
using Integround.Components.Core.FlatFile.Enums;
using System.Text;

namespace Integround.Components.Core.FlatFile.Models
{
    public class FlatFileSchemaElement
    {
        public string Name { get; }
        public string Namespace { get; }
        public FlatFileSchemaElement Parent { get; }

        public byte[] StartElement { get; private set; }
        public byte[] ElementCloser { get; private set; }
        public byte[] EmptyElementCloser { get; private set; }
        public byte[] EndElement { get; private set; }

        public string ValueType { get; set; }
        public SchemaElementType ElementType { get; set; }

        public int MinOccurs { get; private set; }
        public int MaxOccurs { get; }

        public int SequenceNumber { get; set; }
        public FlatFileElementStructure Structure { get; set; }
        public FlatFileElementChildOrder ChildOrder { get; set; }
        public byte[] ChildDelimiter { get; set; }
        public byte[] TagName { get; set; }
        public FlatFileElementJustification Justification { get; set; }
        public int PosLength { get; set; }
        public int PosOffset { get; set; }

        public List<FlatFileSchemaElement> Children { get; }

        public List<byte[]> EndDelimiters
        {
            get
            {
                var delimiters = new List<byte[]>();

                // If there is no parent, this element ends to end-of-file:
                if (Parent == null)
                    delimiters.Insert(0, null);
                else if (Parent.Structure == FlatFileElementStructure.Delimited)
                {
                    // If the parent element is postfix, this element always ends with the parent's child delimiter:
                    if (Parent.ChildOrder == FlatFileElementChildOrder.Postfix)
                        delimiters.Insert(0, Parent.ChildDelimiter);
                    else
                    {
                        // If the parent element is infix/prefix and this is the last element, also parent's delimiters are possible:
                        if (Parent.Children.Count == SequenceNumber)
                        {
                            delimiters.InsertRange(0, Parent.EndDelimiters);

                            // If this element is repeating element, also allow parent's child delimiter:
                            if (MaxOccurs != 1)
                                delimiters.Insert(0, Parent.ChildDelimiter);
                        }
                        // If this is not the last infix/prefix child, delimiter is parent's child delimiter:
                        else
                            delimiters.Insert(0, Parent.ChildDelimiter);
                    }
                }
                return delimiters;
            }
        }

        public FlatFileSchemaElement(string name, string ns, int minOccurs, int maxOccurs, FlatFileSchemaElement parent, Encoding encoding)
        {
            Name = name;
            Namespace = ns;
            Parent = parent;
            MinOccurs = minOccurs;
            MaxOccurs = maxOccurs;

            // Update element names:

            // If the element has the same namespace than its parent, it does not need to be repeated:
            if ((Parent != null) && (Namespace == Parent.Namespace))
            {
                StartElement = encoding.GetBytes(string.Concat("<", Name));
            }
            // Else if the element has a namespace but it differs from parent's ns, write it: 
            else if (Namespace != null)
            {
                StartElement = encoding.GetBytes($"<{Name} xmlns=\"{Namespace}\"");
            }
            // Else element has an empty namespace:
            else
            {
                StartElement = encoding.GetBytes($"<{Name} xmlns=\"\"");
            }

            EndElement = encoding.GetBytes("</" + Name + ">");
            ElementCloser = encoding.GetBytes(">");
            EmptyElementCloser = encoding.GetBytes(" />");

            Children = new List<FlatFileSchemaElement>();
        }

        public FlatFileSchemaElement(string name, string ns, FlatFileSchemaElement parent, Encoding encoding)
        {
            Name = name;
            Namespace = ns;
            Parent = parent;
            MinOccurs = 1;
            MaxOccurs = 1;
            ElementType = SchemaElementType.Attribute;

            StartElement = encoding.GetBytes(" " + Name + "=\"");
            ElementCloser = encoding.GetBytes("\"");
        }
    }
}
