using System;
using System.Collections.Generic;
using System.Linq;

namespace Integround.Components.Log
{
    public class AggregateLogger : ILogger
    {
        private readonly List<ILogger> _loggers = new List<ILogger>();

        public LoggingLevel LoggingLevel => _loggers.Min(x => x.LoggingLevel);
        
        public void Add(ILogger logger)
        {
            _loggers.Add(logger);
        }

        public void Debug(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.Debug(message, exception));
        }

        public void Info(string message)
        {
            _loggers.ForEach(x => x.Info(message));
        }

        public void Warning(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.Warning(message, exception));
        }

        public void Error(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.Error(message, exception));
        }

        [Obsolete]
        public void LogInfo(string message)
        {
            _loggers.ForEach(x => x.Info(message));
        }

        [Obsolete]
        public void LogWarning(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.Warning(message, exception));
        }

        [Obsolete]
        public void LogError(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.Error(message, exception));
        }
    }
}
