using System.Threading.Tasks;

namespace Integround.Components.Log
{
    public interface IMessageLogStore
    {
        Task AddAsync<T>(T obj);
    }
}
