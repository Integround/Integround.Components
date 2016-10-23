using System.Threading.Tasks;
using Integround.Components.Log;

namespace Integround.Components.Core
{
    public interface IProcess
    {
        string Name { get; }
        ProcessStatus Status { get; }
        void Initialize(ProcessConfiguration configuration = null, ILogger logger = null, IMessageLogStore messageLogStore = null);
        void Start(params Message[] msgs);
        void Stop();
    }

    public interface ICallableProcess : IProcess
    {
        Task<Message> CallAsync(params Message[] args);
    }
}
