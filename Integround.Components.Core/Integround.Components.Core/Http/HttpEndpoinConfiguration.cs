using System;
using Integround.Components.Core;

namespace Integround.Components.Http
{
    public class HttpEndpoinConfiguration
    {
        public Guid Id { get; private set; }
        public HttpMethod Method { get; set; }
        public string Path { get; set; }
        public ICallableProcess Process { get; set; }
        public Authentication Authentication { get; set; }
        public HttpEndpoinConfiguration()
        {
            Id = Guid.NewGuid();
        }
    }
}
