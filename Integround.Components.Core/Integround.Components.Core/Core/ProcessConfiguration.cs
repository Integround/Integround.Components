using System.Collections.Generic;

namespace Integround.Components.Core
{
    public class ProcessConfiguration
    {
        public string Name { get; set; }
        public ProcessStatus Status { get; set; }
        public ProcessParameters Parameters { get; set; }
    }

    public class ProcessParameters
    {
        private readonly Dictionary<string, string> _parameters;

        public ProcessParameters(IDictionary<string, string> parameters = null)
        {
            _parameters = (parameters != null)
                ? new Dictionary<string, string>(parameters) 
                : new Dictionary<string, string>();
        }

        public string this[string name] => Get(name);

        public string Get(string name)
        {
            return _parameters[name];
        }
    }

    public enum ProcessStatus
    {
        Stopped,
        Started,
    }
}
