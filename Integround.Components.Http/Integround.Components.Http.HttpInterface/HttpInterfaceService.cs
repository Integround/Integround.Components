using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Integround.Components.Http.HttpInterface.Enums;
using Integround.Components.Http.HttpInterface.Models;
using Integround.Components.Log;
using Message = Integround.Components.Core.Message;

namespace Integround.Components.Http.HttpInterface
{
    [ServiceContract]
    public interface IServiceInterface
    {
        [OperationContract]
        [WebGet(UriTemplate = "/{*operation}", BodyStyle = WebMessageBodyStyle.Bare)]
        Task<Stream> Get(string operation);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/{*operation}", BodyStyle = WebMessageBodyStyle.Bare)]
        Task<Stream> Post(string operation, Stream contents);
    }

    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple, IncludeExceptionDetailInFaults = true)]
    public class HttpInterfaceService : IServiceInterface, IHttpInterfaceService
    {
        private readonly ServiceHost _serviceHost;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<Guid, HttpEndpoinConfiguration> _registeredEndpoints = new ConcurrentDictionary<Guid, HttpEndpoinConfiguration>();

        public HttpInterfaceService(IPEndPoint httpEndpoint, IPEndPoint httpsEndpoint, ILogger logger = null)
        {
            _serviceHost = new ServiceHost(this);
            _logger = logger;
            
            // Add http/https endpoints: 
            var webBehavior = new WebHttpBehavior { FaultExceptionEnabled = true };
            if (httpEndpoint != null)
            {
                var httpAddress = new Uri(string.Concat("http://", httpEndpoint));
                var endpointHttp = _serviceHost.AddServiceEndpoint(typeof(IServiceInterface),
                    GetHttpBinding(),
                    httpAddress);
                endpointHttp.EndpointBehaviors.Add(webBehavior);
            }
            if (httpsEndpoint != null)
            {
                var httpsAddress = new Uri(string.Concat("https://", httpsEndpoint));
                var endpointHttps = _serviceHost.AddServiceEndpoint(typeof(IServiceInterface),
                    GetHttpsBinding(),
                    httpsAddress);
                endpointHttps.EndpointBehaviors.Add(webBehavior);
            }
        }

        public async Task StartAsync()
        {
            await Task.Factory.FromAsync(_serviceHost.BeginOpen, _serviceHost.EndOpen, null);
        }

        public async Task StopAsync()
        {
            var wcfRequestSuccessful = false;
            try
            {
                if (_serviceHost.State != CommunicationState.Faulted)
                {
                    await Task.Factory.FromAsync(_serviceHost.BeginClose, _serviceHost.EndClose, null);
                    wcfRequestSuccessful = true;
                }
            }
            finally
            {
                if (!wcfRequestSuccessful)
                    _serviceHost.Abort();
            }
        }

        public void Register(HttpEndpoinConfiguration endpointConfiguration)
        {
            // Add the endpoint to registration list
            _registeredEndpoints.TryAdd(endpointConfiguration.Id, endpointConfiguration);
        }

        public void Unregister(HttpEndpoinConfiguration endpointConfiguration)
        {
            HttpEndpoinConfiguration removedObject;

            // Add the endpoint to registration list
            _registeredEndpoints.TryRemove(endpointConfiguration.Id, out removedObject);
        }

        public async Task<Stream> Get(string operation)
        {
            // Get the process that is registered to this endpoint and execute it.
            var endpointSetting = _registeredEndpoints.FirstOrDefault(x => (x.Value.Method == HttpMethod.Get) && string.Equals(x.Value.Path, operation));

            if (endpointSetting.Equals(default(KeyValuePair<Guid, HttpEndpoinConfiguration>)))
                throw new Exception($"GET requests to operation '{operation}' are not allowed.");

            var endpoint = endpointSetting.Value;

            var context = WebOperationContext.Current;
            if (context == null)
                throw new Exception("Could not read HTTP context");

            var headers = context.IncomingRequest.Headers;
            var acceptHeader = ParseAcceptHeader(headers);
            var acceptEncoding = ParseAcceptEncodingHeader(headers);

            // Authorize request if configured
            if ((endpoint.Authentication != null) && (endpoint.Authentication.Type != AuthenticationType.None))
            {
                var authorizationValue = headers.Get("Authorization");
                try
                {
                    AuthenticateRequest(authorizationValue, endpoint);
                }
                catch (AuthenticationException ex)
                {
                    context.OutgoingResponse.StatusCode = HttpStatusCode.Unauthorized;
                    return BuildFailureResponse(ex.Message, acceptHeader, acceptEncoding);
                }
            }

            try
            {
                var request = new Request
                {
                    Method = "GET",
                    Url = new Url
                    {
                        BaseUrl = context.IncomingRequest.UriTemplateMatch.BaseUri.AbsoluteUri,
                        Path = context.IncomingRequest.UriTemplateMatch.RequestUri.AbsolutePath
                    }
                };

                // Parse the query parameters if they exist:
                if ((context.IncomingRequest.UriTemplateMatch.QueryParameters != null) &&
                    (context.IncomingRequest.UriTemplateMatch.QueryParameters.Count > 0))
                {
                    request.Url.Query = new List<Parameter>();
                    foreach (string key in context.IncomingRequest.UriTemplateMatch.QueryParameters)
                        request.Url.Query.Add(new Parameter
                        {
                            Name = key,
                            Value = context.IncomingRequest.UriTemplateMatch.QueryParameters[key]
                        });
                }

                // Execute the process:
                var requestMessage = Message.CreateFromObject(request);
                var responseMessage = await endpoint.Process.CallAsync(requestMessage);
                var response = Message.ExtractObject<Response>(responseMessage);

                return BuildSuccessResponse(response, acceptHeader, acceptEncoding);
            }
            catch (Exception ex)
            {
                context.OutgoingResponse.StatusCode = HttpStatusCode.InternalServerError;
                return BuildFailureResponse(
                    string.Concat("An error occurred while executing the process.\n", ex.Message),
                    acceptHeader,
                    acceptEncoding);
            }
        }

        public async Task<Stream> Post(string operation, Stream contents)
        {
            // Get the process that is registered to this endpoint and execute it.
            var endpointSetting = _registeredEndpoints.FirstOrDefault(x => (x.Value.Method == HttpMethod.Post) && string.Equals(x.Value.Path, operation));

            if (endpointSetting.Equals(default(KeyValuePair<Guid, HttpEndpoinConfiguration>)))
                throw new Exception($"POST requests to operation '{operation}' are not allowed.");

            var endpoint = endpointSetting.Value;

            var context = WebOperationContext.Current;
            if (context == null)
                throw new Exception("Could not read HTTP context");

            var headers = context.IncomingRequest.Headers;
            var acceptHeader = ParseAcceptHeader(headers);
            var acceptEncoding = ParseAcceptEncodingHeader(headers);
            var encoding = ParseEncodingHeader(headers);

            // Authorize request if configured
            if ((endpoint.Authentication != null) && (endpoint.Authentication.Type != AuthenticationType.None))
            {
                var authorizationValue = headers.Get("Authorization");
                try
                {
                    AuthenticateRequest(authorizationValue, endpoint);
                }
                catch (AuthenticationException ex)
                {
                    context.OutgoingResponse.StatusCode = HttpStatusCode.Unauthorized;
                    return BuildFailureResponse(ex.Message, acceptHeader, acceptEncoding);
                }
            }

            try
            {
                var request = new Request
                {
                    Method = "POST",
                    Url = new Url
                    {
                        BaseUrl = context.IncomingRequest.UriTemplateMatch.BaseUri.AbsoluteUri,
                        Path = context.IncomingRequest.UriTemplateMatch.RequestUri.AbsolutePath
                    }
                };

                // Parse the query parameters if they exist:
                if ((context.IncomingRequest.UriTemplateMatch.QueryParameters != null) &&
                    (context.IncomingRequest.UriTemplateMatch.QueryParameters.Count > 0))
                {
                    request.Url.Query = new List<Parameter>();
                    foreach (string key in context.IncomingRequest.UriTemplateMatch.QueryParameters)
                        request.Url.Query.Add(new Parameter
                        {
                            Name = key,
                            Value = context.IncomingRequest.UriTemplateMatch.QueryParameters[key]
                        });
                }

                if (contents != null)
                {
                    request.Body = new Body
                    {
                        ContentType = headers.Get("Content-Type"),
                        Payload = new Payload()
                    };

                    // Read the raw contents:
                    using (contents)
                    using (var memStream = new MemoryStream())
                    {
                        await contents.CopyToAsync(memStream);
                        request.Body.Payload.Data = encoding.GetString(memStream.ToArray());
                    }

                    // Try to serialize the request data:
                    try
                    {
                        switch (DetermineContentType(request.Body.ContentType))
                        {
                            case RequestContentType.Json:
                                request.Body.Payload.XmlData = Json.JsonConverter.ConvertToXml(request.Body.Payload.Data).DocumentElement;
                                break;
                            case RequestContentType.Xml:
                                var xmlDoc = new XmlDocument();
                                xmlDoc.LoadXml(request.Body.Payload.Data);
                                request.Body.Payload.XmlData = xmlDoc.DocumentElement;
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        // TODO: Log warning?
                        //status = InstanceStatus.Warning;
                        //stepInfo.AddWarningMessage(String.Format("Request serialization was unsuccessful: {0}", ExceptionHelper.GetMessage(ex)));
                    }
                }

                // Execute the process:
                var requestMessage = Message.CreateFromObject(request);
                var responseMessage = await endpoint.Process.CallAsync(requestMessage);
                var response = Message.ExtractObject<Response>(responseMessage);

                return BuildSuccessResponse(response, acceptHeader, acceptEncoding);
            }
            catch (Exception ex)
            {
                context.OutgoingResponse.StatusCode = HttpStatusCode.InternalServerError;
                return BuildFailureResponse(
                    string.Concat("An error occurred while executing the process.\n", ex.Message),
                    acceptHeader,
                    acceptEncoding);
            }
        }

        private static Binding GetHttpBinding()
        {
            var result = new CustomBinding(new WebHttpBinding
            {
                ContentTypeMapper = new RawMapper()
            });

            return result;
        }

        private static Binding GetHttpsBinding()
        {
            var result = new CustomBinding(new WebHttpBinding(WebHttpSecurityMode.Transport)
            {
                ContentTypeMapper = new RawMapper(),
                Security = new WebHttpSecurity
                {
                    Mode = WebHttpSecurityMode.Transport,
                    Transport = new HttpTransportSecurity
                    {
                        ClientCredentialType = HttpClientCredentialType.None
                    }
                },
            });

            return result;
        }

        /// <summary>
        /// Determines content type from string
        /// </summary>
        /// <param name="contentType">Content type as string</param>
        /// <returns>Typed content type <see cref="RequestContentType"/></returns>
        private static RequestContentType DetermineContentType(string contentType)
        {
            if ((contentType.IndexOf("application/json", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("application/javascript", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("application/x-json", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("application/x-javascript", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("text/json", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("text/javascript", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("text/x-json", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("text/x-javascript", StringComparison.CurrentCultureIgnoreCase) != -1))
            {
                return RequestContentType.Json;
            }
            else if ((contentType.IndexOf("text/xml", StringComparison.CurrentCultureIgnoreCase) != -1) ||
                (contentType.IndexOf("application/xml", StringComparison.CurrentCultureIgnoreCase) != -1))
            {
                return RequestContentType.Xml;
            }

            return RequestContentType.Unknown;
        }

        /// <summary>
        /// Authenticates a request.
        /// </summary>
        /// <param name="authorizationHeader">Authorization header</param>
        /// <param name="endpoint">Endpoint configuration</param>
        /// <exception cref="AuthenticationException">Thrown if authentication failed</exception>
        private void AuthenticateRequest(string authorizationHeader, HttpEndpoinConfiguration endpoint)
        {
            if ((endpoint.Authentication == null) || (endpoint.Authentication.Type == AuthenticationType.None))
                return;

            switch (endpoint.Authentication.Type)
            {
                case AuthenticationType.Basic:
                    if (!string.IsNullOrWhiteSpace(authorizationHeader) && authorizationHeader.StartsWith("Basic "))
                    {
                        // Get the username & password from the header:

                        string credentialsStr;
                        try
                        {
                            var credentialsBase64 = authorizationHeader.Substring(6).Trim(); // Strip the "Basic " string from auth header
                            credentialsStr = Encoding.UTF8.GetString(Convert.FromBase64String(credentialsBase64));
                        }
                        catch (FormatException ex)
                        {
                            _logger.LogWarning("Client credentials were incorrectly encoded to base64 format", ex);
                            throw new AuthenticationException("Username and password are required to be in Authorization header in base64 encoded format");
                        }

                        var credentials = credentialsStr.Split(':');
                        if (credentials.Length != 2)
                        {
                            _logger.LogWarning("Client didn't provide credentials in a correct 'username:password' format");
                            throw new AuthenticationException("Username or password was incorrect");
                        }

                        var username = credentials[0];
                        var password = credentials[1];

                        // Check if the header credentials match the configured endpoint credentials:

                        if (!(string.Equals(endpoint.Authentication.Credentials.UserName, username) &&
                            string.Equals(endpoint.Authentication.Credentials.Password, password)))
                        {
                            _logger.LogWarning("Client provided incorrect username or password");
                            throw new AuthenticationException("Username or password was incorrect");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Client didn't provide credentials for basic authenticated REST interface");
                        throw new AuthenticationException("This service requires basic authentication credentials in Authorization header");
                    }
                    break;
                case AuthenticationType.ApiToken:
                    if (!string.IsNullOrWhiteSpace(authorizationHeader) && authorizationHeader.StartsWith("Bearer "))
                    {
                        var bearerToken = authorizationHeader.Substring(7); // Strip the "Bearer " string from auth header
                        if (!string.Equals(bearerToken, endpoint.Authentication.ApiToken))
                        {
                            _logger.LogWarning("Client didn't provide correct bearer token");
                            throw new AuthenticationException("Bearer token was incorrect");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Client didn't provide authorization header");
                        throw new AuthenticationException("This service requires bearer Authorization header");
                    }
                    break;
            }
        }

        /// <summary>
        /// Parses the accept header and returns content type
        /// </summary>
        /// <param name="headers">Request headers</param>
        /// <returns>Accept type</returns>
        private static RequestContentType ParseAcceptHeader(WebHeaderCollection headers)
        {
            var acceptHeader = headers.Get("Accept");
            if (acceptHeader != null)
                return DetermineContentType(acceptHeader);

            // Use XML by default:
            return RequestContentType.Xml;
        }

        /// <summary>
        /// Parses the Accept-Charset header and returns the encoding value
        /// </summary>
        /// <param name="headers">Request headers</param>
        /// <returns>Accept encoding value</returns>
        private static Encoding ParseAcceptEncodingHeader(WebHeaderCollection headers)
        {
            var header = headers.Get("Accept-Charset");
            if (!string.IsNullOrWhiteSpace(header))
            {
                return Encoding.GetEncoding(header);
            }

            return Encoding.GetEncoding("ISO-8859-1");
        }

        /// <summary>
        /// Parses the Content-Type header and returns the encoding value
        /// </summary>
        /// <param name="headers">Request headers</param>
        /// <returns>Encoding value</returns>
        private static Encoding ParseEncodingHeader(WebHeaderCollection headers)
        {
            var header = headers.Get("Content-Type");

            if (!string.IsNullOrWhiteSpace(header))
            {
                var contentTypeHeader = MediaTypeHeaderValue.Parse(header);

                if (!string.IsNullOrWhiteSpace(contentTypeHeader.CharSet))
                    return Encoding.GetEncoding(contentTypeHeader.CharSet);
            }

            return Encoding.GetEncoding("ISO-8859-1");
        }

        /// <summary>
        /// Builds a memory stream from the response body.
        /// </summary>
        /// <param name="response">Response message</param>
        /// <param name="acceptContentType">Content type to use for serialization</param>
        /// <param name="acceptEncoding">Response encoding</param>
        /// <returns>Memory stream from data serialized by content type</returns>
        private static MemoryStream BuildSuccessResponse(Response response, RequestContentType acceptContentType, Encoding acceptEncoding)
        {
            var bodyString = string.Empty;

            // If Body is null, just return empty response
            if (response?.Body == null)
                return new MemoryStream(acceptEncoding.GetBytes(bodyString)) { Position = 0 };

            if (response.Body.Payload.XmlData != null)
            {
                switch (acceptContentType)
                {
                    case RequestContentType.Json:

                        // Remove the namespace attributes:
                        response.Body.Payload.XmlData.Attributes.RemoveNamedItem("xmlns");
                        response.Body.Payload.XmlData.Attributes.RemoveNamedItem("xmlns:xsd");
                        response.Body.Payload.XmlData.Attributes.RemoveNamedItem("xmlns:xsi");
                        bodyString = Json.JsonConverter.ConvertFromXml(response.Body.Payload.XmlData);
                        break;
                    case RequestContentType.Xml:
                        bodyString = response.Body.Payload.XmlData.OuterXml;
                        break;
                }
            }
            else if (!string.IsNullOrEmpty(response.Body.Payload.Data))
            {
                bodyString = XmlString.Decode(response.Body.Payload.Data);
            }

            return new MemoryStream(acceptEncoding.GetBytes(bodyString)) { Position = 0 };
        }

        /// <summary>
        /// Builds a memory stream for failure responses.
        /// </summary>
        /// <param name="errorMessage">Error message</param>
        /// <param name="acceptContentType">Type of serialization</param>
        /// <param name="acceptEncoding">Response encoding</param>
        /// <returns></returns>
        private static MemoryStream BuildFailureResponse(string errorMessage, RequestContentType acceptContentType, Encoding acceptEncoding)
        {
            MemoryStream responseStream;
            var httpError = new HttpError(errorMessage);
            var serializer = new XmlSerializer(typeof(HttpError));

            switch (acceptContentType)
            {
                case RequestContentType.Json:
                    using (var stream = new MemoryStream())
                    {
                        // Serialize the object to Xml:
                        serializer.Serialize(stream, httpError);
                        stream.Position = 0;

                        // Load the xml stream to a XmlDocument:
                        var xmlDoc = new XmlDocument();
                        xmlDoc.Load(stream);

                        // Remove the namespace attributes:
                        if (xmlDoc.DocumentElement?.Attributes != null)
                        {
                            xmlDoc.DocumentElement.Attributes.RemoveNamedItem("xmlns");
                            xmlDoc.DocumentElement.Attributes.RemoveNamedItem("xmlns:xsd");
                            xmlDoc.DocumentElement.Attributes.RemoveNamedItem("xmlns:xsi");
                        }

                        // Serialize the XmlElement to JSON:
                        var serialized = Json.JsonConverter.ConvertFromXml(xmlDoc.DocumentElement);
                        responseStream = new MemoryStream(acceptEncoding.GetBytes(serialized));
                    }
                    break;
                case RequestContentType.Xml:
                case RequestContentType.Unknown:
                default:
                    responseStream = new MemoryStream();
                    serializer.Serialize(responseStream, httpError);
                    break;
            }
            responseStream.Position = 0;

            return responseStream;
        }
    }

    public class RawMapper : WebContentTypeMapper
    {
        /// <summary>
        /// Always returns raw
        /// </summary>
        /// <param name="contentType">Content type of request, doesn't really matter though</param>
        /// <returns>RAW</returns>
        public override WebContentFormat GetMessageFormatForContentType(string contentType)
        {
            // enables untyped data in requests
            return WebContentFormat.Raw;
        }
    }
}
