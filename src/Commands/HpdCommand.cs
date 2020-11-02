using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NetEbics.Exceptions;
using NetEbics.Handler;
using NetEbics.Parameters;
using NetEbics.Responses;
using NetEbics.Xml;
using Version = NetEbics.Responses.Version;

namespace NetEbics.Commands
{
    internal class HpdCommand : GenericCommand<HpdResponse>
    {
        private static readonly ILogger s_logger = EbicsLogging.CreateLogger<HpdCommand>();
        private string _transactionId;

        internal HpdParams Params { private get; set; }
        internal override string OrderType => "HPD";
        internal override string OrderAttribute => "DZHNN";
        internal override TransactionType TransactionType => TransactionType.Download;
        internal override IList<XmlDocument> Requests => null;
        internal override XmlDocument InitRequest => CreateInitRequest();
        internal override XmlDocument ReceiptRequest => CreateReceiptRequest();

        internal override DeserializeResponse Deserialize(string payload)
        {
            using (new MethodLogger(s_logger))
            {
                try
                {
                    var dr = base.Deserialize(payload);

                    if (dr.HasError || dr.IsRecoverySync)
                    {
                        return dr;
                    }

                    if (dr.Phase != TransactionPhase.Initialisation)
                    {
                        return dr;
                    }

                    _transactionId = dr.TransactionId;

                    var doc = XDocument.Parse(payload);
                    var xph = new XPathHelper(doc, Namespaces);

                    var decryptedOd = DecryptOrderData(xph);
                    var deflatedOd = Decompress(decryptedOd);
                    var strResp = Encoding.UTF8.GetString(deflatedOd);
                    var hpdrod = XDocument.Parse(strResp);
                    var r = new XPathHelper(hpdrod, Namespaces);

                    s_logger.LogDebug("Order data:\n{orderData}", hpdrod.ToString());

                    var urlList = new List<Url>();
                    var xmlUrls = r.GetAccessParamsUrls();
                    foreach (var xmlUrl in xmlUrls)
                    {
                        var validFrom = ParseISO8601Timestamp(xmlUrl.Attribute(XmlNames.valid_from)?.Value);
                        urlList.Add(new Url {ValidFrom = validFrom, Address = xmlUrl.Value});
                    }

                    var accessParams = new AccessParams
                    {
                        Urls = urlList,
                        HostId = r.GetAccessParamsHostId()?.Value,
                        Institute = r.GetAccessParamsInstitute()?.Value
                    };

                    var version = new Version
                    {
                        Protocols = r.GetProtocolParamsProtocol()?.Value.Trim().Split(" ").ToList(),
                        Authentications = r.GetProtocolParamsAuthentication()?.Value.Trim().Split(" ").ToList(),
                        Encryptions = r.GetProtocolParamsEncryption()?.Value.Trim().Split(" ").ToList(),
                        Signatures =  r.GetProtocolParamsSignature()?.Value.Trim().Split(" ").ToList()
                    };

                    bool.TryParse(r.GetProtocolParamsRecovery()?.Attribute(XmlNames.supported)?.Value,
                        out var recoverySupp);
                    bool.TryParse(r.GetProtocolParamsPreValidation()?.Attribute(XmlNames.supported)?.Value,
                        out var preValidationSupp);
                    bool.TryParse(r.GetProtocolParamsX509Data()?.Attribute(XmlNames.supported)?.Value,
                        out var x509Supp);
                    bool.TryParse(r.GetProtocolParamsX509Data()?.Attribute(XmlNames.persistent)?.Value,
                        out var x509Pers);
                    bool.TryParse(r.GetProtocolParamsClientDataDownload()?.Attribute(XmlNames.supported)?.Value,
                        out var clientDataDownloadSupp);
                    bool.TryParse(r.GetProtocolParamsDownloadableOrderData()?.Attribute(XmlNames.supported)?.Value,
                        out var downloadableOrderDataSupp);

                    var protocolParams = new ProtocolParams
                    {
                        Version = version,
                        RecoverySupported = recoverySupp,
                        PreValidationSupported = preValidationSupp,
                        X509DataSupported = x509Supp,
                        X509DataPersistent = x509Pers,
                        ClientDataDownloadSupported = clientDataDownloadSupp,
                        DownloadableOrderDataSupported = downloadableOrderDataSupp                        
                    };

                    Response.AccessParams = accessParams;
                    Response.ProtocolParams = protocolParams;
                    
                    return dr;
                }
                catch (EbicsException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new DeserializationException($"can't deserialize {OrderType} command", ex, payload);
                }
            }
        }

        private XmlDocument CreateReceiptRequest()
        {
            try
            {
                var receiptReq = new EbicsRequest
                {
                    Version = Config.Version,
                    Revision = Config.Revision,
                    Namespaces = Namespaces,
                    StaticHeader = new StaticHeader
                    {
                        Namespaces = Namespaces,
                        HostId = Config.User.HostId,
                        TransactionId = _transactionId
                    },
                    MutableHeader = new MutableHeader
                    {
                        Namespaces = Namespaces,
                        TransactionPhase = "Receipt"
                    },
                    Body = new Body
                    {
                        Namespaces = Namespaces,
                        TransferReceipt = new TransferReceipt
                        {
                            Namespaces = Namespaces,
                            ReceiptCode = "0"
                        }
                    }
                };

                return AuthenticateXml(receiptReq.Serialize().ToXmlDocument(), null, null);
            }
            catch (EbicsException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CreateRequestException($"can't create receipt request for {OrderType}", ex);
            }
        }

        private XmlDocument CreateInitRequest()
        {
            using (new MethodLogger(s_logger))
            {
                try
                {
                    var initReq = new EbicsRequest
                    {
                        StaticHeader = new StaticHeader
                        {
                            Namespaces = Namespaces,
                            HostId = Config.User.HostId,
                            PartnerId = Config.User.PartnerId,
                            UserId = Config.User.UserId,
                            SecurityMedium = Params.SecurityMedium,
                            Nonce = CryptoUtils.GetNonce(),
                            Timestamp = CryptoUtils.GetUtcTimeNow(),
                            BankPubKeyDigests = new BankPubKeyDigests
                            {
                                Namespaces = Namespaces,
                                Bank = Config.Bank,
                                DigestAlgorithm = s_digestAlg
                            },
                            OrderDetails = new OrderDetails
                            {
                                Namespaces = Namespaces,
                                OrderAttribute = OrderAttribute,
                                OrderType = OrderType,
                                StandardOrderParams = new EmptyOrderParams
                                {
                                    Namespaces = Namespaces,
                                }
                            }
                        },
                        MutableHeader = new MutableHeader
                        {
                            Namespaces = Namespaces,
                            TransactionPhase = "Initialisation"
                        },
                        Body = new Body
                        {
                            Namespaces = Namespaces
                        },
                        Namespaces = Namespaces,
                        Version = Config.Version,
                        Revision = Config.Revision,
                    };

                    return AuthenticateXml(initReq.Serialize().ToXmlDocument(), null, null);
                }
                catch (EbicsException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CreateRequestException($"can't create init request for {OrderType}", ex);
                }
            }
        }
    }
}