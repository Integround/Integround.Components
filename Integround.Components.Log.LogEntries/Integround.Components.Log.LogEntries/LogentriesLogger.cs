using System;
using Serilog;

namespace Integround.Components.Log.LogEntries
{
    public class LogentriesLogger : ILogger
    {
        private readonly Serilog.ILogger _log;
        
        public LoggingLevel LoggingLevel { get; private set; }

        public LogentriesLogger(string token, LoggingLevel level = LoggingLevel.Info)
        {
            LoggingLevel = level;

            var conf = new LoggerConfiguration().WriteTo.Logentries(token);
            switch(level)
            {
                case LoggingLevel.Debug:
                    conf.MinimumLevel.Debug();
                    break;
                case LoggingLevel.Info:
                    conf.MinimumLevel.Information();
                    break;
                case LoggingLevel.Warning:
                    conf.MinimumLevel.Warning();
                    break;
                case LoggingLevel.Error:
                    conf.MinimumLevel.Error();
                    break;
            }
            _log = conf.CreateLogger();
        }

        public void Debug(string message, Exception exception = null)
        {
            if (exception != null)
                _log.Debug(exception, message);
            else
                _log.Debug(message);
        }

        public void Info(string message)
        {
            _log.Information(message);
        }

        public void Warning(string message, Exception exception = null)
        {
            if (exception != null)
                _log.Warning(exception, message);
            else
                _log.Warning(message);
        }

        public void Error(string message, Exception exception = null)
        {
            if (exception != null)
                _log.Error(exception, message);
            else
                _log.Error(message);
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
