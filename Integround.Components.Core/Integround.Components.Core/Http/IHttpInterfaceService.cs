namespace Integround.Components.Http
{
    public interface IHttpInterfaceService
    {
        void Register(HttpEndpoinConfiguration endpointConfiguration);
        void Unregister(HttpEndpoinConfiguration endpointConfiguration);
    }
}
