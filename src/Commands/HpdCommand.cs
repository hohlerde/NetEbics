using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NetEbics.Exceptions;
using NetEbics.Handler;
using NetEbics.Parameters;
using NetEbics.Responses;
using NetEbics.Xml;
using Org.BouncyCastle.Asn1.Cms;

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