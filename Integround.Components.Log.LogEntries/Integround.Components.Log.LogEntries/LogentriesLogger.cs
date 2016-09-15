using System;
using Serilog;

namespace Integround.Components.Log.LogEntries
{
    public class LogentriesLogger : ILogger
    {
        private readonly Serilog.ILogger _log;

        public LogentriesLogger(string token)
        {
            _log = new LoggerConfiguration()
                .WriteTo.Logentries(token)
                .CreateLogger();
        }

        public void LogError(string message, Exception exception = null)
        {
            if (exception != null)
                _log.Error(exception, message);
            else
                _log.Error(message);
        }

        public void LogInfo(string message)
        {
            _log.Information(message);
        }

        public void LogWarning(string message, Exception exception = null)
        {
            if (exception != null)
                _log.Warning(exception, message);
            else
                _log.Warning(message);
        }
    }
}
