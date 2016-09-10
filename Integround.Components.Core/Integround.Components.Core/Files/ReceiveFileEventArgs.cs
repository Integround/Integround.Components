using System;

namespace Integround.Components.Files
{
    public class ReceiveFileEventArgs : EventArgs
    {
        public ReceiveFileResult File { get; private set; }

        public ReceiveFileEventArgs(ReceiveFileResult file)
        {
            File = file;
        }
    }
}
