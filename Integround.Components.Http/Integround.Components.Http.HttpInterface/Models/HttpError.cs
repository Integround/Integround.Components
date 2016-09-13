using System;
using System.Xml.Serialization;

namespace Integround.Components.Http.HttpInterface.Models
{
    [XmlRoot(ElementName = "HttpError", Namespace = "http://schemas.integround.com/httpinterface/2016-01-05")]
    public class HttpError
    {
        public string ErrorMessage { get; set; }

        [Obsolete("Used by serialization")]
        public HttpError() { }

        public HttpError(string message)
        {
            ErrorMessage = message;
        }
    }
}
