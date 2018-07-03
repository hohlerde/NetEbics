/*
 * NetEbics -- .NET Core EBICS Client Library
 * (c) Copyright 2018 Bjoern Kuensting
 *
 * This file is subject to the terms and conditions defined in
 * file 'LICENSE.txt', which is part of this source code package.
 */

using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NetEbics.Config;
using NetEbics.Handler;
using NetEbics.Parameters;
using NetEbics.Responses;

namespace NetEbics
{
    public class EbicsClientFactory
    {
        private Func<EbicsConfig, IEbicsClient> _ctor;

        public EbicsClientFactory(Func<EbicsConfig, IEbicsClient> ctor)
        {
            _ctor = ctor;
        }

        public IEbicsClient Create(EbicsConfig cfg)
        {
            return _ctor(cfg);
        }
    }

    public class EbicsClient : IEbicsClient, IDisposable
    {
        private static readonly ILogger Logger = EbicsLogging.CreateLogger<EbicsClient>();
        private EbicsConfig _config;
        private HttpClient _httpClient;
        private HttpClientHandler _httpClientHandler;
        private readonly ProtocolHandler _protocolHandler;
        private readonly CommandHandler _commandHandler;
        
        protected volatile bool _disposed;
        
        public EbicsConfig Config
        {
            get => _config;
            set
            {
                _config = value ?? throw new ArgumentNullException(nameof(Config));
                _httpClientHandler = new HttpClientHandler();
                if (_config.Insecure)
                {
                    _httpClientHandler.ServerCertificateCustomValidationCallback =
                        (message, certificate2, chain, error) =>
                        {
                            Logger.LogDebug($"Ignoring certificate error {error}");
                            return true;
                        };
                }

                _httpClient = new HttpClient(_httpClientHandler) {BaseAddress = new Uri(_config.Address)};
                _commandHandler.Config = value;
                _protocolHandler.Client = _httpClient;
                _commandHandler.ProtocolHandler = _protocolHandler;
                var nsCfg = new NamespaceConfig();
                switch (_config.Version)
                {
                    case EbicsVersion.H004:
                        nsCfg.Ebics = $"urn:org:ebics:{EbicsVersion.H004.ToString()}";
                        break;
                    case EbicsVersion.H005:
                        nsCfg.Ebics = $"urn:org:ebics:{EbicsVersion.H005.ToString()}";
                        break;
                    default:
                        throw new ArgumentException(nameof(Config.Version));
                }

                nsCfg.XmlDsig = "http://www.w3.org/2000/09/xmldsig#";
                nsCfg.Cct = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";
                nsCfg.Cdd = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02";
                nsCfg.SignatureData = "http://www.ebics.org/S001";

                _commandHandler.Namespaces = nsCfg;
            }
        }

        public static EbicsClientFactory Factory()
        {
            return new EbicsClientFactory(x => new EbicsClient {Config = x});
        }

        private EbicsClient()
        {
            _protocolHandler = new ProtocolHandler();
            _commandHandler = new CommandHandler();
        }

        ~EbicsClient()
        {
            Dispose(false);
        }

        public HpbResponse HPB(HpbParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<HpbResponse>(p);
                return resp;
            }
        }

        public PtkResponse PTK(PtkParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<PtkResponse>(p);
                return resp;
            }
        }

        public StaResponse STA(StaParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<StaResponse>(p);
                return resp;
            }
        }

        public CctResponse CCT(CctParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<CctResponse>(p);
                return resp;
            }
        }

        public IniResponse INI(IniParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<IniResponse>(p);
                return resp;
            }
        }

        public HiaResponse HIA(HiaParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<HiaResponse>(p);
                return resp;
            }
        }

        public SprResponse SPR(SprParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<SprResponse>(p);
                return resp;
            }
        }

        public CddResponse CDD(CddParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<CddResponse>(p);
                return resp;
            }
        }

        public HpdResponse HPD(HpdParams p)
        {
            using (new MethodLogger(Logger))
            {
                var resp = _commandHandler.Send<HpdResponse>(p);
                return resp;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            _httpClientHandler?.Dispose();
            _httpClient?.Dispose();
        }
    }
}