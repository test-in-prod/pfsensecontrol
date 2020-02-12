using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;

namespace PfSenseControl
{
    [DebuggerDisplay("Name = {Name}")]
    public sealed class GatewayTableListItem
    {

        public string Name
        {
            get;
            private set;
        }

        public string Interface
        {
            get;
            private set;
        }

        public string GatewayIPAddress
        {
            get;
            private set;
        }

        public string MonitorIPAddress
        {
            get;
            private set;
        }

        public string Description
        {
            get;
            private set;
        }

        internal GatewayTableListItem(HtmlNode trNode)
        {
            var tdNodes = trNode.ChildNodes.Where(n => n.Name == "td").ToArray();
            Name = tdNodes[2].InnerText.Trim();
            Interface = tdNodes[4].InnerText.Trim();
            GatewayIPAddress = tdNodes[5].InnerText.Trim();
            MonitorIPAddress = tdNodes[6].InnerText.Trim();
            Description = tdNodes[7].InnerText.Trim();
        }

    }
}
