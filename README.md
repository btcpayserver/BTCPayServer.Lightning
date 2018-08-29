# A C# library for Lightning Network clients

## Introduction

This library is meant to facilitate the development of Lightning Network based apps written in C#.
It is composed of 4 packages.

* `BTCPayServer.Lightning.All` super package which reference all the others [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.All.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.All)
* `BTCPayServer.Lightning.LND` exposes easy to use LND clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.LND.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.LND)
* `BTCPayServer.Lightning.CLightning` exposes easy to use clightning clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.CLightning.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.CLightning)
* `BTCPayServer.Lightning.Charge` exposes easy to use Charge clients [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Charge.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Charge)
* `BTCPayServer.Lightning.Common` exposes common classes and `ILightningClient` [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Common.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Common)

If you develop an app, we advise you to reference `BTCPayServer.Lightning.All` [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.All.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.All).

If you develop a library, we advise you to reference `BTCPayServer.Lightning.Common` [![NuGet](https://img.shields.io/nuget/v/BTCPayServer.Lightning.Common.svg)](https://www.nuget.org/packages/BTCPayServer.Lightning.Common).

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

The `connectionString` encapsulates the necessary information BTCPay needs to connect to your lightning node, we currently support:

* `clightning` via TCP or unix domain socket connection
* `lightning charge` via HTTPS
* `LND` via the REST proxy

#### Examples

* `type`=clightning;`server`=unix://root/.lightning/lightning-rpc
* `type`=clightning;`server`=tcp://1.1.1.1:27743/
* `type`=lnd-rest;`server`=<span>http://mylnd:8080/</span>;`macaroonfilepath`=/root/.lnd/admin.macaroon;allowinsecure=true
* `type`=lnd-rest;`server`=<span>https://mylnd:8080/</span>;`macaroon`=abef263adfe...
* `type`=lnd-rest;`server`=<span>https://mylnd:8080/</span>;`macaroon`=abef263adfe...;`certthumbprint`=abef263adfe...
* `type`=charge;`server`=<span>https://charge:8080/</span>;`api-token`=myapitoken...

Note that the `certthumbprint` to connect to your LND node can be obtained through this command line:

```bash
openssl x509 -noout -fingerprint -sha256 -inform pem -in /root/.lnd/tls.cert
```

You can omit `certthumprint` if you the certificate is trusted by your machine

You can set `allowinsecure` to `true` if your LND REST server is using HTTP or HTTPS with an untrusted certificate which you don't know the `certthumprint`.

### Using implementation specific class

If you want to leverage specific lightning network implementation, either instanciate directly `ChargeClient`, `LndClient` or `CLightningClient`, or cast the `ILightningClient` object returned by `LightningClientFactory`.

## How to test

You first need to run all the dependencies with docker-compose:

```bash
cd tests
docker-compose up
```

Then you can run and debug the tests with visual studio or visual studio code.

If you want to use command line:
```bash
cd tests
dotnet test
```

## Licence

[MIT](LICENSE)