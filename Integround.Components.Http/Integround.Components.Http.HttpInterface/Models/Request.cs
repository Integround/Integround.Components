using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;

namespace Integround.Components.Http.HttpInterface.Models
{
    [XmlRoot(ElementName = "Request", Namespace = "http://schemas.integround.com/httpinterface/2016-01-05")]
    public class Request
    {
        [XmlAttribute("id")]
        public string Id { get; set; }
        public string Method { get; set; }
        public Url Url { get; set; }
        public List<Header> Headers { get; set; }
        public Body Body { get; set; }
    }
    
    public class Url
    {
        public string BaseUrl { get; set; }
        public string Path { get; set; }
        public List<Parameter> Query { get; set; }
    }

    public class Parameter
    {
        [XmlAttribute("name")]
        public string Name { get; set; }
        [XmlText]
        public string Value { get; set; }
    }

    public class Header
    {
        [XmlAttribute("name")]
        public string Name { get; set; }
        [XmlText]
        public string Value { get; set; }
    }

    public class Body
    {
        public string ContentType { get; set; }
        public string Encoding { get; set; }
        public string Checksum { get; set; }
        public Payload Payload { get; set; }
    }

    public class Payload
    {
        public string Data { get; set; }
        public XmlElement XmlData { get; set; }
        public List<Parameter> FormData { get; set; }
    }

    [XmlRoot(ElementName = "Response", Namespace = "http://schemas.integround.com/httpinterface/2016-01-05")]
    public class Response
    {
        [XmlAttribute("id")]
        public string Id { get; set; }
        public string StatusCode { get; set; }
        public string StatusMessage { get; set; }
        public List<Header> Headers { get; set; }
        public Body Body { get; set; }
    }
}
