using System;
using System.Diagnostics;

namespace Integround.Components.Log
{
    public class TraceLogger : ILogger
    {
        public void LogError(string message, Exception exception = null)
        {
            Trace.TraceInformation((exception != null)
                ? $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | ERROR | {message} | {exception}"
                : $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | ERROR | {message}");
        }

        public void LogInfo(string message)
        {
            Trace.TraceInformation($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | INFO | {message}");
        }

        public void LogWarning(string message, Exception exception = null)
        {
            Trace.TraceInformation((exception != null)
                ? $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | WARNING | {message} | {exception}"
                : $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | WARNING | {message}");
        }
    }
}
