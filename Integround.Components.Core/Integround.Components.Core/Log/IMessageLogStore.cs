using System.Threading.Tasks;

namespace Integround.Components.Log
{
    public interface IMessageLogStore
    {
        LoggingLevel LoggingLevel { get; }

        Task<string> AddAsync<T>(T obj);
        Task<string> AddDebugAsync<T>(T obj);
    }
}
