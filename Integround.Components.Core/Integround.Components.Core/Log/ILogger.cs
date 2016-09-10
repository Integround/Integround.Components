using System;

namespace Integround.Components.Log
{
    public interface ILogger
    {
        void LogInfo(string message);
        void LogWarning(string message, Exception exception = null);
        void LogError(string message, Exception exception = null);
    }
}
