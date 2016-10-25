using System.Threading.Tasks;

namespace Integround.Components.Log
{
    public interface IMessageLogStore
    {
        LoggingLevel LoggingLevel { get; }

        Task<string> AddAsync<T>(T obj);
        Task<string> AddAsync<T>(T obj, string fileName);
        Task<string> AddDebugAsync<T>(T obj);
        Task<string> AddDebugAsync<T>(T obj, string fileName);

        Task<int> CleanUp(int maxMessageAgeInDays);
    }
}
