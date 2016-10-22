using System;
using System.Diagnostics;

namespace Integround.Components.Log
{
    public class TraceLogger : ILogger
    {
        public LoggingLevel LoggingLevel { get; private set; }

        public TraceLogger(LoggingLevel level = LoggingLevel.Info)
        {
            LoggingLevel = level;
        }

        public void Debug(string message, Exception exception = null)
        {
            if (LoggingLevel > LoggingLevel.Debug)
                return;

            Trace.TraceInformation((exception != null)
                ? $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | DEBUG | {message} | {exception}"
                : $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | DEBUG | {message}");
        }

        public void Info(string message)
        {
            if (LoggingLevel > LoggingLevel.Info)
                return;

            Trace.TraceInformation($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | INFO | {message}");
        }

        public void Warning(string message, Exception exception = null)
        {
            if (LoggingLevel > LoggingLevel.Warning)
                return;

            Trace.TraceInformation((exception != null)
                ? $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | WARNING | {message} | {exception}"
                : $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | WARNING | {message}");
        }

        public void Error(string message, Exception exception = null)
        {
            Trace.TraceInformation((exception != null)
                ? $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | ERROR | {message} | {exception}"
                : $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | ERROR | {message}");
        }

        [Obsolete]
        public void LogInfo(string message)
        {
            Info(message);
        }

        [Obsolete]
        public void LogWarning(string message, Exception exception = null)
        {
            Warning(message, exception);
        }

        [Obsolete]
        public void LogError(string message, Exception exception = null)
        {
            Error(message, exception);
        }
    }
}
