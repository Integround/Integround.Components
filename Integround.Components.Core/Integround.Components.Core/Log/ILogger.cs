using System;

namespace Integround.Components.Log
{
    public interface ILogger
    {
        LoggingLevel LoggingLevel { get; }
        
        void Debug(string message, Exception exception = null);
        void Info(string message);
        void Warning(string message, Exception exception = null);
        void Error(string message, Exception exception = null);

        [Obsolete]
        void LogInfo(string message);
        [Obsolete]
        void LogWarning(string message, Exception exception = null);
        [Obsolete]
        void LogError(string message, Exception exception = null);
    }
}
