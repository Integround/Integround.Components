using System;

namespace Integround.Components.Core
{
    public interface IScheduler
    {
        event EventHandler<MessageEventArgs> Trigger;
        
        void Start();
        void Stop();
    }
}
