using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace PfSenseControl
{
    public sealed class SystemGateways
    {
        readonly List<GatewayTableListItem> gateways = null;

        /// <summary>
        /// Gets the gateways table
        /// </summary>
        public IEnumerable<GatewayTableListItem> Gateways
        {
            get
            {
                return gateways.AsReadOnly();
            }
        }

        /// <summary>
        /// Select the gateway or gatewaygroup to use as the default gateway.
        /// </summary>
        public DefaultGatewayList DefaultGatewayIPv4 { get; private set; }
        /// <summary>
        /// Select the gateway or gatewaygroup to use as the default gateway.
        /// </summary>
        public DefaultGatewayList DefaultGatewayIPv6 { get; private set; }

        internal SystemGateways(string htmlBody)
        {
            var parser = new HtmlDocument();
            parser.LoadHtml(htmlBody);

            gateways = parseGatewaysTable(parser);

            DefaultGatewayIPv4 = parseList(parser, "defaultgw4");
            DefaultGatewayIPv6 = parseList(parser, "defaultgw6");
        }

        private static List<GatewayTableListItem> parseGatewaysTable(HtmlDocument parser)
        {
            var list = new List<GatewayTableListItem>();

            var gatewaysTable = parser.DocumentNode.SelectSingleNode("//table[@id='gateways']");
            if (gatewaysTable == null)
                throw new InvalidOperationException("Gateways table was not found, this is a bug");

            var tbody = gatewaysTable.ChildNodes.FindFirst("tbody");
            if (tbody == null)
                throw new InvalidOperationException("Gateways table does not contain a tbody element, this is a bug");

            foreach (var tr in tbody.ChildNodes.Where(n => n.Name == "tr"))
            {
                var item = new GatewayTableListItem(tr);
                list.Add(item);
            }
            return list;
        }

        private static DefaultGatewayList parseList(HtmlDocument parser, string fieldName)
        {
            var select = parser.DocumentNode.SelectSingleNode($"//select[@name='{fieldName}']");
            if (select == null)
                throw new InvalidOperationException($"Unable to parse html, cannot find select element named {fieldName}, this is a bug");

            var list = new DefaultGatewayList();
            foreach (var opt in select.ChildNodes)
            {
                if (opt.Name == "option")
                {
                    var item = new DefaultGatewayListItem() { InternalName = opt.Attributes["value"].Value, Name = opt.InnerText };
                    list.items.Add(item);
                    if (opt.Attributes["selected"] != null)
                    {
                        list.Selected = item;
                    }
                }
            }
            return list;
        }

    }
}
