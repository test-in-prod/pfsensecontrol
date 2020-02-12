# pfsensecontrol
pfSense "API" emulator for C#

Since [pfSense](https://www.pfsense.org/) itself does not have a programmatic API, this is essentially a high-level API created for controlling pfSense appliance.

The "API" presented here is mostly a crude browser emulator in that [HttpClient](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient?view=netcore-3.1) along with [Html Agility Pack](https://html-agility-pack.net/) are used to perform necessary calls to pfSense web UI and scrape off the necessary data to make it useful in an OOP context.

## WARNING: experimental stuff

This project started because one day my primary WAN access died and my LTE backup was cumbersome to reconfigure to keep internet connectivity.
I then wanted a sort of simple "toggle switch" that can be used to quickly failover to LTE backup interface in case the primary cable modem WAN goes bad again.

There's a couple of things I wanted to determine in this scenario:

1 - Determine the "quality" of WAN connection first if failover makes sense (excessive packet loss over last 5 minutes)
2 - Change default gateway to LTE instead of WAN
3 - Reset whole-house VPN connection (ProtonVPN)
4 - Reset states table to re-establish connectivity
5 - Enable certain firewall rules to reduce traffic via LTE to conserve bandwidth and costs (LTE is expensive!)

... and vise-versa in case WAN comes back up, I want a simple "manual transfer switch" that can do all this work.

## What's implemented

So, here's the status of it today.

There's the main `PfSenseContext` which can be used to `Login` to the appliance. This class then has methods that mimick the Web GUI (e.g. `GetSystemGateways()` == System > Gateways in UI).

Under the hood, `HttpClient` is used to make HTTP calls, with `HtmlDocument` to parse the result and make it useful in OOP-way or for further scripting.

** Tested & developed with pfSense 2.4.4-RELEASE-p3 as of February 12, 2020 **

## Example

```csharp
using (var context = new PfSenseContext("https://pfsensebox/", new System.Net.NetworkCredential("usernamehere", "passwordhere")))
{
	context.Login().GetAwaiter().GetResult(); // or use await if method is async
	var gateways = context.GetSystemGateways().GetAwaiter().GetResult();
	
	// gateways mirrors the information presented on System > Routing > Gateways page at https://pfsensebox/system_gateways.php
}
```
