using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using Integround.Components.Http.RestClient.Models;
using Integround.Components.Log;

namespace Integround.Components.Http.RestClient
{
    public class RestClient
    {
        private readonly string _url;
        private readonly HttpMethod? _method;
        private readonly string _contentType;
        private readonly ILogger _logger;

        public AuthenticationType AuthenticationType { get; set; }
        public Credentials Credentials { get; set; }

        public int RetryCount { get; set; }

        public RestClient(string url = null, HttpMethod? method = null, string contentType = null, ILogger logger = null)
        {
            _url = url;
            _method = method;
            _contentType = contentType;
            _logger = logger;
        }

        public async Task<Response> CallOperationAsync(Request request)
        {
            Response resp;

            // Send the request to the interface:
            HttpMethod requestMethod;
            UriBuilder url;
            var bodyString = "";
            string contentType;

            var httpClient = new HttpClient();
            try
            {
                // Settings defined in the input messages override the values set in the step parameters!

                // Set the request method:
                if (string.IsNullOrWhiteSpace(request.Method))
                    requestMethod = _method ?? HttpMethod.Get;
                else
                    requestMethod = GetDeliveryMethod(request.Method);

                // Set the base URL:
                url = string.IsNullOrWhiteSpace(request.Url?.BaseUrl)
                    ? new UriBuilder(_url)
                    : new UriBuilder(request.Url.BaseUrl);

                // Add the URL parameters:
                if (request.Url?.Query != null)
                {
                    var query = string.IsNullOrWhiteSpace(url.Query) ? "" : url.Query.Substring(1);

                    foreach (var parameter in request.Url.Query)
                    {
                        if (!string.IsNullOrWhiteSpace(query))
                            query += "&";
                        query += parameter.Name + "=" + parameter.Value;
                    }

                    url.Query = query;
                }

                // Set the content type:
                if (string.IsNullOrWhiteSpace(request.Body?.ContentType))
                    contentType = string.IsNullOrWhiteSpace(_contentType)
                        ? "application/xml; charset=utf-8"
                        : _contentType;
                else
                    contentType = request.Body.ContentType;


                // Get the body:
                if (request.Body?.Payload != null)
                {
                    if (request.Body.Payload.XmlData != null)
                    {
                        bodyString = contentType.Contains("application/json")
                            ? Json.JsonConverter.ConvertFromXml(request.Body.Payload.XmlData)
                            : request.Body.Payload.XmlData.OuterXml;
                    }
                    else if (request.Body.Payload.FormData != null && request.Body.Payload.FormData.Count > 0)
                    {
                        foreach (var param in request.Body.Payload.FormData)
                        {
                            if (!string.IsNullOrWhiteSpace(bodyString))
                                bodyString += "&";

                            bodyString += HttpUtility.UrlEncode(param.Name) + "=" + HttpUtility.UrlEncode(param.Value);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(request.Body.Payload.Data))
                        bodyString = XmlString.Decode(request.Body.Payload.Data);
                }

                // Set authorization settings:
                if ((request.Authentication != null) &&
                    (request.Authentication.Type == AuthenticationType.Basic) &&
                    (request.Authentication.Credentials != null))
                {
                    var authorizationString = string.Join(":", request.Authentication.Credentials.UserName, request.Authentication.Credentials.Password);
                    var byteArray = Encoding.ASCII.GetBytes(authorizationString);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                }
                else if ((AuthenticationType == AuthenticationType.Basic) &&
                    (Credentials != null))
                {
                    var authorizationString = string.Join(":", Credentials.UserName, Credentials.Password);
                    var byteArray = Encoding.ASCII.GetBytes(authorizationString);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                }

                // Set the request headers:
                if (request.Headers != null)
                    foreach (var h in request.Headers)
                        httpClient.DefaultRequestHeaders.Add(h.Name, h.Value);

                // Set the timeout if it was defined:
                if (request.Timeout.HasValue)
                    httpClient.Timeout = TimeSpan.FromSeconds(request.Timeout.Value);

            }
            catch (Exception ex)
            {
                throw new Exception("Initializing the REST request failed.", ex);
            }


            HttpResponseMessage response;
            // Execute the REST request:
            using (httpClient)
            {
                try
                {
                    response = await ExecuteRestRequestAsync(httpClient, requestMethod, url.Uri, bodyString, contentType);
                }
                catch (Exception ex)
                {
                    throw new Exception("Sending the request failed.", ex);
                }

                if (((int)response.StatusCode < 200) || ((int)response.StatusCode >= 300))
                {
                    throw new Exception("The server responded with an error " + (int)response.StatusCode + ": " + response.ReasonPhrase);
                }
            }


            // Handle the response:
            try
            {
                resp = new Response { Id = request.Id };

                using (response)
                {
                    resp.StatusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                    resp.StatusMessage = response.ReasonPhrase;

                    // If the response has content, read it & convert it to XML if necessary
                    if (response.Content?.Headers?.ContentType?.MediaType != null)
                    {
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var reader = new StreamReader(stream))
                        {
                            resp.Body = new Body
                            {
                                ContentType = response.Content.Headers.ContentType.MediaType,
                                Encoding = response.Content.Headers.ContentType.CharSet,
                                Payload = new Payload()
                            };

                            if (response.Content.Headers.ContentType.MediaType.StartsWith("application/json") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("application/javascript") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("application/x-json") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("application/x-javascript") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("text/json") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("text/javascript") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("text/x-json") ||
                                response.Content.Headers.ContentType.MediaType.StartsWith("text/x-javascript"))
                            {
                                // Convert JSON to XML

                                // Read the response to a string:
                                resp.Body.Payload.Data = await reader.ReadToEndAsync();

                                // Convert the string to XML:
                                var responseXml = Json.JsonConverter.ConvertToXml(resp.Body.Payload.Data);
                                resp.Body.Payload.XmlData = responseXml.DocumentElement;
                            }
                            else if (response.Content.Headers.ContentType.MediaType.StartsWith("text/html"))
                            {
                                // content-type text/html is a bit tricky. you never know what you get.
                                // essentially we're making our own root-element, so the response can be xml/text/json/whateva
                                var responseString = await reader.ReadToEndAsync();
                                using (var xmlReader = XmlReader.Create(new StringReader(
                                    $"<Response>{System.Security.SecurityElement.Escape(responseString)}</Response>")))
                                {
                                    var xmlDoc = new XmlDocument();
                                    xmlDoc.Load(xmlReader);
                                    resp.Body.Payload.XmlData = xmlDoc.DocumentElement;
                                }
                            }
                            else
                            {
                                // Read the response to XML:
                                using (var xmlReader = XmlReader.Create(reader))
                                {
                                    var xmlDoc = new XmlDocument();
                                    xmlDoc.Load(xmlReader);
                                    resp.Body.Payload.XmlData = xmlDoc.DocumentElement;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Reading the response failed.", ex);
            }

            return resp;
        }

        private async Task<HttpResponseMessage> ExecuteRestRequestAsync(HttpClient httpClient, HttpMethod requestMethod, Uri url, string body, string contentType)
        {
            HttpResponseMessage response = null;
            var contentTypeHeader = MediaTypeHeaderValue.Parse(contentType);

            var encoding = Encoding.GetEncoding("ISO-8859-1");
            if (!string.IsNullOrWhiteSpace(contentTypeHeader.CharSet))
                encoding = Encoding.GetEncoding(contentTypeHeader.CharSet);

            var retries = 0;
            var retryCount = RetryCount;
            while (retries <= retryCount)
            {
                retries++;
                var sleepTime = (int)Math.Pow(retries, retries) * 1000;

                try
                {
                    StringContent content;
                    switch (requestMethod)
                    {
                        case HttpMethod.Get:
                            response = await httpClient.GetAsync(url);
                            break;
                        case HttpMethod.Post:
                            // Set the request contents:
                            content = new StringContent(body, encoding);
                            content.Headers.ContentType = contentTypeHeader;
                            response = await httpClient.PostAsync(url, content);
                            break;
                        case HttpMethod.Put:
                            // Set the request contents:
                            content = new StringContent(body, encoding);
                            content.Headers.ContentType = contentTypeHeader;
                            response = await httpClient.PutAsync(url, content);
                            break;
                        case HttpMethod.Delete:
                            response = await httpClient.DeleteAsync(url);
                            break;
                        case HttpMethod.Head:
                            content = new StringContent(body, encoding);
                            content.Headers.ContentType = contentTypeHeader;
                            using (var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Head, url) { Content = content })
                            {
                                response = await httpClient.SendAsync(request);
                            }
                            break;
                        case HttpMethod.Options:
                            content = new StringContent(body, encoding);
                            content.Headers.ContentType = contentTypeHeader;
                            using (var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Options, url) { Content = content })
                            {
                                response = await httpClient.SendAsync(request);
                            }
                            break;
                        case HttpMethod.Patch:
                            content = new StringContent(body, encoding);
                            content.Headers.ContentType = contentTypeHeader;
                            using (var request = new HttpRequestMessage(new System.Net.Http.HttpMethod("PATCH"), url) { Content = content })
                            {
                                response = await httpClient.SendAsync(request);
                            }
                            break;
                    }
                    break;
                }
                catch (Exception ex)
                {
                    var method = requestMethod.ToString().ToUpper();

                    // If this is not the last retry, log a warning.
                    if (retries <= retryCount)
                    {
                        _logger?.Warning($"Executing the HTTP {method} request failed. Retrying {retries}/{retryCount} in {sleepTime} ms.");
                    }
                    // Otherwise, throw an exception
                    else if (retryCount == 0)
                        throw new Exception($"Executing the HTTP {method} request failed.", ex);
                    else
                        throw new Exception($"Executing the HTTP {method} request failed after {retryCount} retries.", ex);
                }

                // Sleep before retrying:
                await Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(sleepTime);
                });
            }

            return response;
        }

        private static HttpMethod GetDeliveryMethod(string methodString)
        {
            HttpMethod method;
            switch (methodString.ToUpper())
            {
                case "GET":
                    method = HttpMethod.Get;
                    break;
                case "POST":
                    method = HttpMethod.Post;
                    break;
                case "PUT":
                    method = HttpMethod.Put;
                    break;
                case "DELETE":
                    method = HttpMethod.Delete;
                    break;
                case "PATCH":
                    method = HttpMethod.Patch;
                    break;
                case "HEAD":
                    method = HttpMethod.Head;
                    break;
                case "OPTIONS":
                    method = HttpMethod.Options;
                    break;
                default:
                    throw new Exception("Method '" + methodString + "' is not supported.");
            }

            return method;
        }
    }
}
