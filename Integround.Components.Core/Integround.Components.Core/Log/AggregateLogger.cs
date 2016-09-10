using System;
using System.Collections.Generic;
namespace Integround.Components.Log
{
    public class AggregateLogger : ILogger
    {
        private readonly List<ILogger> _loggers = new List<ILogger>();

        public void Add(ILogger logger)
        {
            _loggers.Add(logger);
        }

        public void LogError(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.LogError(message, exception));
        }

        public void LogInfo(string message)
        {
            _loggers.ForEach(x => x.LogInfo(message));
        }

        public void LogWarning(string message, Exception exception = null)
        {
            _loggers.ForEach(x => x.LogWarning(message, exception));
        }
    }
}
