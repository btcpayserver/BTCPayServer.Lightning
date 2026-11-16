[![CI](https://github.com/btcpayserver/BTCPayServer.Lightning/actions/workflows/ci.yml/badge.svg)](https://github.com/btcpayserver/BTCPayServer.Lightning/actions/workflows/ci.yml)

# A C# library for Lightning Network clients

## Introduction

This library is meant to facilitate the development of Lightning Network based apps written in C#.
Its main goal isn't to perfectly implement client API of all lightning node implementation, but to provide a common abstraction that can be used to support many implementations at once.

The most important packages are:

* `BTCPayServer.Lightning.Common` contains the common abstractions shared between those implementation.
* `BTCPayServer.Lightning.All` contains `LightningClientFactory` which can convert a connection string to an instance of `ILightningClient`.

If your app depends on `ILightningClient` directly, you will be able to support those different lightning implementations out of the box.

Here is a description of all packages:

* `BTCPayServer.Lightning.All` super package which reference all the others [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.All.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.All)
* `BTCPayServer.Lightning.Common` exposes common classes and `ILightningClient` [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Common.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Common)
* `BTCPayServer.Lightning.LND` exposes easy to use LND clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.LND.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.LND)
* `BTCPayServer.Lightning.CLightning` exposes easy to use clightning clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.CLightning.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.CLightning)
* `BTCPayServer.Lightning.Eclair` exposes easy to use Eclair clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Eclair.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Eclair)
* `BTCPayServer.Lightning.LNDhub` exposes easy to use LNDhub clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.LNDhub.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.LNDhub)

If you develop an app, we advise you to reference `BTCPayServer.Lightning.All` [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.All.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.All).

If you develop a library, we advise you to reference `BTCPayServer.Lightning.Common` [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Common.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Common).

You can also use our `BOLT11PaymentRequest` to parse BOLT11 invoices. (See [example](https://github.com/btcpayserver/BTCPayServer.Lightning/blob/master/tests/CommonTests.cs#L139)).

## How to use

Click on the nuget button of the package interesting you, and follow the instruction to add it to your project.
For .NET Core Apps, you need to enter this in your project's folder:

```bash
dotnet add package BTCPayServer.Lightning.All
```

You have two ways to use this library:

* Either you want your code to works with all lightning implementation (right now LND, CLightning, Eclair, LNDHub, Phoenixd)
* Or you want your code to work on a particular lightning implementation

### Using the generic interface

This is done by using `LightningClientFactory` and the common interface `ILightningClient`.

```csharp
string connectionString = "...";
ILightningClientFactory factory = new LightningClientFactory(Network.Main);
ILightningClient client = factory.Create(connectionString);
LightningInvoice invoice = await client.CreateInvoice(10000, "CanCreateInvoice", TimeSpan.FromMinutes(5));
```

`ILightningClient` is an interface which abstract the underlying implementation with a common interface.

The `connectionString` encapsulates the necessary information BTCPay needs to connect to your Lightning node, we currently support:

* `clightning` via TCP or unix domain socket connection, or via clnrest with a rune
* `LND` via the REST proxy
* `Eclair` via their new REST API
* `LNDhub` via their REST API

#### Examples

* `type=clightning;server=unix://root/.lightning/lightning-rpc`
* `type=clightning;server=tcp://1.1.1.1:27743/`
* `type=clightning;server=https://127.0.0.1:3010/;rune=tU-RLjMiDpY2U0o3W1oFowar36RFGpWloPbW9-RuZdo9MA==;certthumbprint=abef263adfe...`
* `type=clightning;server=https://127.0.0.1:3010/;runefilepath=/var/lib/btcpayserver/clightning.rune;certfilepath=/var/lib/clightning/bitcoin/server.pem`
* `type=clightning;server=http://10.10.0.2:3010/;rune=tU-RLjMiDpY2U0o3W1oFowar36RFGpWloPbW9-RuZdo9MA==;allowinsecure=true`
* `type=lnd-rest;server=http://mylnd:8080/;macaroonfilepath=/root/.lnd/invoice.macaroon;allowinsecure=true`
* `type=lnd-rest;server=https://mylnd:8080/;macaroon=abef263adfe...`
* `type=lnd-rest;server=https://mylnd:8080/;macaroon=abef263adfe...;certthumbprint=abef263adfe...`
* `type=lnd-rest;server=https://mylnd:8080/;macaroonfilepath=/root/.lnd/invoice.macaroon;certfilepath=/var/lib/lnd/tls.cert`
* `type=eclair;server=http://127.0.0.1:4570;password=eclairpass`
* `type=eclair;server=http://127.0.0.1:4570;password=eclairpass;bitcoin-host=bitcoin.host;bitcoin-auth=btcpass`
* `type=lndhub;server=https://login:password@lndhub.io`

##### Eclair notes

Note that `bitcoin-host` and `bitcoin-auth` are optional, only useful if you want to call `ILightningClient.GetDepositAddress` on Eclair.
We expect this won't be needed in the future.

##### Core Lightning notes

An `http://` or `https://` server connects through the [clnrest](https://docs.corelightning.org/docs/rest) plugin.
clnrest needs a rune, given by `rune` or by `runefilepath` (a `.rune` file, read on each request).
A rune can limit what BTCPay Server may do. This one only allows receiving payments:

```bash
lightning-cli -k createrune restrictions='[["method=getinfo","method=invoice","method=listinvoices","method=waitanyinvoice","method=delinvoice","method=listfunds","method=listpeerchannels"],["method/delinvoice","pnamestatus=unpaid"]]'
```

Invoice watching (`Listen`) needs `listinvoices` and `waitanyinvoice`.
Paying invoices also needs `xpay` and `listpays`.

`certthumbprint`, `certfilepath` and `allowinsecure=true` set how the client checks the clnrest certificate.
They differ from LND in a few ways.
The pin is on the leaf certificate, a pin wins over `allowinsecure=true`, and a pin needs an `https://` server.
`certfilepath` is read once, when the client is created, so a renewed certificate needs a new client.
clnrest serves its `server.pem` without the CA that signed it, so the thumbprint is the one of `server.pem`:

```bash
openssl x509 -noout -fingerprint -sha256 -in ~/.lightning/bitcoin/server.pem | sed -e 's/.*=//;s/://g'
```

The server has to be the root URL of clnrest.
An `HttpClient` you pass needs a timeout of at least 2 seconds, and invoice watching keeps each wait shorter than it.
Error messages from clnrest carry only a valid lightningd error's code and its message, cut to 200 characters.
The client reads at most 128 MB per response, enough for `listinvoices` with about 160k paid invoices. On a bigger node, set up `autoclean` for paid invoices.
`runefilepath` and `certfilepath` read files on the machine that runs the client, and the rune goes to the configured server.
An app must not let untrusted users set them.
If you pass your own `HttpClient`, it must not follow redirects, or it sends the rune to the redirect target.

The client needs Core Lightning 24.11 or newer, because it pays with `xpay`.
It sends named parameters (Core Lightning 0.12 or newer for these names) and uses the `timeout` of `waitanyinvoice` (0.8.1 or newer).
clnrest is tested with Core Lightning 26.06.

##### LND notes

Unless the LND certificate is trusted by your machine you need to set the server authentication scheme for LND by specifying one of: `certthumbprint`, `certfilepath`, `allowinsecure=true`.

`certfilepath` and `certthumbprint` are equivalent in terms of security but differ in their practical usage.
`certfilepath` reads a file on your file system so unless you set up some kind of file syncing you can not use it for remote connections.
The advantage is that if the certificate is updated (and synced in case of a remote machine) no more reconfiguration is required for RPC connections to continue to work.
It is therefore the recommended option if you can use it.

The `certthumbprint` to connect to your LND node can be obtained through this command line:

```bash
openssl x509 -noout -fingerprint -sha256 -in /root/.lnd/tls.cert | sed -e 's/.*=//;s/://g'
```

`allowinsecure=true` just blindly accepts any server connection and is therefore not secure unless used in tightly controlled environments.
E.g. host is the same machine or is accessed over an encrypted tunnel, assuming no untrusted entities can bind the specified port.

##### LNDhub notes

You can also use the `lndhub://` URL, which can be retrieved e.g. from the BlueWallet Export/Backup screen.
The library turns it into the expected `type=lndhub` connection string format.

### Using implementation specific class

If you want to leverage specific lightning network implementation, either instanciate directly `LndClient`, `CLightningClient`, or `EclairLightningClient`, or cast the `ILightningClient` object returned by `LightningClientFactory`.

## How to test

You first need to run all the dependencies with docker-compose:

```bash
cd tests
docker-compose up
```

Then you can run and debug the tests with Visual Studio or Visual Studio Code.

If you want to use command line:

```bash
cd tests
dotnet test
```

## Licence

[MIT](LICENSE)
