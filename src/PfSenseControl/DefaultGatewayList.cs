using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace PfSenseControl
{
    [DebuggerDisplay("{Selected.Name}")]
    public sealed class DefaultGatewayList
    {
        public const string InternalAutomatic = "";
        public const string InternalNone = "-";

        internal List<DefaultGatewayListItem> items = new List<DefaultGatewayListItem>();

        public DefaultGatewayListItem Selected
        {
            get;
            internal set;
        }

        public IEnumerable<DefaultGatewayListItem> Items
        {
            get
            {
                return items.AsReadOnly();
            }
        }

        public DefaultGatewayListItem this[string internalName]
        {
            get
            {
                return items.FirstOrDefault(x => x.InternalName == internalName);
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("Null is not supported");
                if (items.Any(x => x.InternalName == value.InternalName))
                {
                    Selected = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException("Item is not in the Items list");
                }
            }
        }


    }
}
