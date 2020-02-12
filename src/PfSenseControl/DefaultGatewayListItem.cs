using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PfSenseControl
{
    [DebuggerDisplay("{Name}")]
    public sealed class DefaultGatewayListItem
    {

        public string InternalName
        {
            get;
            internal set;
        }

        public string Name
        {
            get;
            internal set;
        }

    }
}
