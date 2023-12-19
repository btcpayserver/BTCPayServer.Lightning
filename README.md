[![CircleCI](https://circleci.com/gh/btcpayserver/BTCPayServer.Lightning.svg?style=svg)](https://circleci.com/gh/btcpayserver/BTCPayServer.Lightning)

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
* `BTCPayServer.Lightning.Charge` exposes easy to use Charge clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Charge.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Charge)
* `BTCPayServer.Lightning.Eclair` exposes easy to use Eclair clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Eclair.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Eclair)
* `BTCPayServer.Lightning.LNbank` exposes easy to use LNbank clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.LNbank.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.LNbank)
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

* Either you want your code to works with all lightning implementation (right now LND, Charge, CLightning)
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

* `clightning` via TCP or unix domain socket connection
* `lightning charge` via HTTPS
* `LND` via the REST proxy
* `Eclair` via their new REST API
* `LNbank` via REST API
* `LNDhub` via their REST API

#### Examples

* `type=clightning;server=unix://root/.lightning/lightning-rpc`
* `type=clightning;server=tcp://1.1.1.1:27743/`
* `type=lnd-rest;server=http://mylnd:8080/;macaroonfilepath=/root/.lnd/invoice.macaroon;allowinsecure=true`
* `type=lnd-rest;server=https://mylnd:8080/;macaroon=abef263adfe...`
* `type=lnd-rest;server=https://mylnd:8080/;macaroon=abef263adfe...;certthumbprint=abef263adfe...`
* `type=lnd-rest;server=https://mylnd:8080/;macaroonfilepath=/root/.lnd/invoice.macaroon;certfilepath=/var/lib/lnd/tls.cert`
* `type=charge;server=https://charge:8080/;api-token=myapitoken...`
* `type=charge;server=https://charge:8080/;cookiefilepath=/path/to/cookie...`
* `type=eclair;server=http://127.0.0.1:4570;password=eclairpass`
* `type=eclair;server=http://127.0.0.1:4570;password=eclairpass;bitcoin-host=bitcoin.host;bitcoin-auth=btcpass`
* `type=lnbank;server=http://lnbank:5000;api-token=myapitoken;allowinsecure=true`
* `type=lnbank;server=https://mybtcpay.com/lnbank;api-token=myapitoken`
* `type=lndhub;server=https://login:password@lndhub.io`

##### Eclair notes

Note that `bitcoin-host` and `bitcoin-auth` are optional, only useful if you want to call `ILightningClient.GetDepositAddress` on Eclair.
We expect this won't be needed in the future.

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

If you want to leverage specific lightning network implementation, either instanciate directly `ChargeClient`, `LndClient` or `CLightningClient`, or cast the `ILightningClient` object returned by `LightningClientFactory`.

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
