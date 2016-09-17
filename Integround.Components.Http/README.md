# Integround.Components.Http
**Integround.Components** is a set of libraries containing tools for building integration processes. HTTP library contains components for publishing HTTP interfaces and consuming REST services.

**Http.HttpInterface** component is used to publish an HTTP interace from an application. Pass the http/https System.Net.IPEndpoint objects to HttpInterfaceService constructor and your HTTP service is up.

**Http.RestClient** component is used to consume REST interfaces from your application or integration process. Just create a RestClient instance and you are ready to call REST services.