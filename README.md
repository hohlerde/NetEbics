# Important

**This repository has moved to https://codeberg.org/hohlerde/NetEbics**

# NetEbics -- .NET Core EBICS Client Library

This is a client library implementing the [EBICS](http://www.ebics.org) protocol for business online banking.

The library is written in C# (7.2) using .NET Core 2.x and was tested with private/public keys (PEM files) on Linux/Windows. 

**NetEbics is alpha software and should not be used for production**. API/breaking changes are very likely. 

## Limitations

* Usage with certificates has been prepared but not completely implemented yet. NetEbics works with private/public keys.
* Only version A005 for signatures can be used. A006 uses PSS padding, which is currently not supported by .NET Core 2.x. Bouncy Castle is only used for PEM file and certificate management.
* Only version E002 for encryption can be used.
* Only version X002 for authentication can be used.
* NetEbics was developed using EBICS Version H004, but H005 should work.
* Currently implemented commands/requests: INI, HIA, HPB, PTK, SPR, STA, CCT, CDD

## Dependencies

NetEbics is denpendent on the following libraries:

* [BouncyCastle](https://www.bouncycastle.org/csharp/) (used for PEM file and X509 certificate management) 
* [Zlib](https://archive.codeplex.com/?p=dotnetzip) (used for un-/compressing EBICS order data)
* Microsoft Extension Logging 
* Microsoft Xml Cryptography (used for basic xml security)
* [StatePrinter](https://github.com/kbilsted/StatePrinter) (used for debug logs)

NetEbics doesn't use dependency injection. See the csproj file for further information.

## Installation

Right now there are no official NuGet packages available, so you have to build the library on your own by cloning this repository.

Make sure you have the [.NET Core SDK](https://www.microsoft.com/net/learn/get-started/) version 2 or higher installed.

Clone the repository.

```text
git clone https://github.com/hohlerde/NetEbics.git
```

Build the library.

```text
cd NetEbics
dotnet pack -c Release
```

You'll find the NuGet package under `bin/Release`.

## Usage

In order to use the library you should have a reasonable good understanding of the EBICS protocol.

### Initialization (INI/HIA)
The first thing you want to do as a new EBICS user is to announce your public RSA keys to your bank. You need to create three public/private key pairs for this (authentication, signature and encryption keys).

Creating the keys is easy with BouncyCastle.

```csharp
var gen = GeneratorUtilities.GetKeyPairGenerator("RSA");
gen.Init(new KeyGenerationParameters(new SecureRandom(), 4096));
var signKeyPair = gen.GenerateKeyPair();   

using (TextWriter sw = new StreamWriter("sign.key"))
{
    var pw = new PemWriter(sw);
    pw.WriteObject(signKeyPair);
    sw.Flush();
}
```

Adjust the above code and also create an authentication ("auth.key") and encryption ("enc.key") key.

Announce your public signature key to your bank. Note that the previously generated keys are stored in PEM format and contain the private and public key.

```csharp
AsymmetricCipherKeyPair signKey;

using (var sr = new StringReader(File.ReadAllText("sign.key").Trim()))
{
    var pr = new PemReader(sr);
    signKey = pr.ReadObject();
}

var signCert = KeyUtils.CreateX509Certificate2(signKey);

var client = EbicsClient.Factory().Create(new EbicsConfig
{
    Address = "The EBICS URL you got from your bank, i.e. https://ebics-server.com/",
    Insecure = true,
    TLS = true,
    User = new UserParams
    {
        HostId = "The host ID of your bank",
        PartnerId = "Your partner ID you got from your bank",
        UserId = "Your user ID you got from your bank",
        SignKeys = new SignKeyPair
        {
            Version = SignVersion.A005, // only A005 is supported right now
            TimeStamp = DateTime.Now,
            Certificate = signCert // internally we work with keys
        }
    }
});

var resp = c.INI(new IniParams());
```

After that we need to announce the public authentication and encryption keys.

```csharp
// loading of keys "auth.key" and "enc.key" omitted

var authCert = KeyUtils.CreateX509Certificate2(authKey);
var encCert = KeyUtils.CreateX509Certificate2(encKey);

var client = EbicsClient.Factory().Create(new EbicsConfig
{
    Address = "The EBICS URL you got from your bank, i.e. https://ebics-server.com/",
    Insecure = true,
    TLS = true,
    User = new UserParams
    {
        HostId = "The host ID of your bank",
        PartnerId = "Your partner ID",
        UserId = "Your user ID",
        AuthKeys = new AuthKeyPair
        {
            Version = AuthVersion.X002,
            TimeStamp = DateTime.Now,
            Certificate = authCert
        },
        CryptKeys = new CryptKeyPair
        {
            Version = CryptVersion.E002,
            TimeStamp = DateTime.Now,
            Certificate = encCert
        }
    }
});

var resp = c.HIA(new HiaParams());
```

Announcing the keys is not enough, as the bank needs to be sure that the keys really belong to you. To prove this, you need to send the INI and HIA letters to your bank. They contain hash values of your public keys and your written signature. The EBICS specification describes in detail how these letters should look like.

### Retrieving public bank keys (HPB)

In order to communicate via EBICS with the bank you need the bank's public keys, because data exchanged needs to be encrypted and authenticated.

```csharp
// loading of keys "auth.key" and "enc.key" omitted

var authCert = KeyUtils.CreateX509Certificate2(authKey);
var encCert = KeyUtils.CreateX509Certificate2(encKey);

var client = EbicsClient.Factory().Create(new EbicsConfig
{
    Address = "The EBICS URL you got from your bank, i.e. https://ebics-server.com/",
    Insecure = true,
    TLS = true,
    User = new UserParams
    {
        HostId = "The host ID of your bank",
        PartnerId = "Your partner ID",
        UserId = "Your user ID",
        AuthKeys = new AuthKeyPair
        {
            Version = AuthVersion.X002,
            TimeStamp = DateTime.Now,
            Certificate = authCert
        },
        CryptKeys = new CryptKeyPair
        {
            Version = CryptVersion.E002,
            TimeStamp = DateTime.Now,
            Certificate = encCert
        }
    }
});

var hpbResp = c.HPB(new HpbParams());
if (hpbResp.TechnicalReturnCode != 0 || hpbResp.BusinessReturnCode != 0)
{
    // handle error
    return;
}

c.Config.Bank = resp.Bank; // set bank's public keys

// now issue other commands 
```

### Direct credit transfer (CCT)

```csharp
// loading of keys "auth.key", "enc.key" and "sign.key" omitted

var authCert = KeyUtils.CreateX509Certificate2(authKey);
var encCert = KeyUtils.CreateX509Certificate2(encKey);
var signCert = KeyUtils.CreateX509Certificate2(signKey);

var client = EbicsClient.Factory().Create(new EbicsConfig
{
    Address = "The EBICS URL you got from your bank, i.e. https://ebics-server.com/",
    Insecure = true,
    TLS = true,
    User = new UserParams
    {
        HostId = "The host ID of your bank",
        PartnerId = "Your partner ID",
        UserId = "Your user ID",
        AuthKeys = new AuthKeyPair
        {
            Version = AuthVersion.X002,
            TimeStamp = DateTime.Now,
            Certificate = authCert
        },
        CryptKeys = new CryptKeyPair
        {
            Version = CryptVersion.E002,
            TimeStamp = DateTime.Now,
            Certificate = encCert
        },
        SignKeys = new SignKeyPair
        {
            Version = SignVersion.A005,
            TimeStamp = DateTime.Now,
            Certificate = signCert
        }
    }
});

var hpbResp = c.HPB(new HpbParams());
if (hpbResp.TechnicalReturnCode != 0 || hpbResp.BusinessReturnCode != 0)
{
    // handle error
    return;
}

c.Config.Bank = resp.Bank; // set bank's public keys

// create credit transfer data structure

var cctParams = new CctParams
{
    InitiatingParty = "Your name",
    PaymentInfos = new[]
    {
        new CreditTransferPaymentInfo
        {
            DebtorName = "Sender's name",
            DebtorAccount = "Sender's IBAN",
            DebtorAgent = "Sender's BIC",
            ExecutionDate = "2018-05-15",
            CreditTransferTransactionInfos = new[]
            {
                new CreditTransferTransactionInfo
                {
                    Amount = "1.00",
                    CreditorName = "Receiver's name",
                    CreditorAccount = "Receiver's IBAN",
                    CreditorAgent = "Receiver's BIC",
                    CurrencyCode = "EUR",
                    EndToEndId = "something",
                    RemittanceInfo = "Unstructured information for receiver",
                }
            }
        }
    }
};

var cctResp = c.CCT(cctParams);
```

## Logs

If you're not in a ASP.NET environment and want to see some log output you can for example enable Serilog together with Microsoft extensions logging.

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateLogger();

EbicsLogging.MethodLoggingEnabled = true; // see entry/exit messages in log
EbicsLogging.LoggerFactory.AddSerilog();
```

You need to reference `Serilog.Extensions.Logging` and `Serilog.Sinks.Console` in your csproj file to use Serilog.

```xml
<ItemGroup>
    <PackageReference Include="Serilog.Extensions.Logging" Version="2.0.2" />
    <PackageReference Include="Serilog.Sinks.Console" Version="3.1.2-dev-00771" />
</ItemGroup>
```

In an ASP.NET environment you just need to pass the `LoggerFactory` instance you get from the depency injection container to NetEbics.

```csharp
public MyController(ILoggerFactory loggerFactory)
{
    EbicsLogging.MethodLoggingEnabled = true;
    EbicsLogging.LoggerFactory = loggerFactory;
}
```

# License

<a rel="license" href="http://creativecommons.org/licenses/by-nc-sa/4.0/"><img alt="Creative Commons License" style="border-width:0" src="https://i.creativecommons.org/l/by-nc-sa/4.0/88x31.png" /></a><br />This work is licensed under a <a rel="license" href="http://creativecommons.org/licenses/by-nc-sa/4.0/">Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License</a>.

See the file LICENSE.txt for further information.
