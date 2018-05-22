/*
 * NetEbics -- .NET Core EBICS Client Library
 * (c) Copyright 2018 Bjoern Kuensting
 *
 * This file is subject to the terms and conditions defined in
 * file 'LICENSE.txt', which is part of this source code package.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NetEbics.Exceptions;
using NetEbics.Parameters;
using NetEbics.Responses;
using NetEbics.Xml;

namespace NetEbics.Commands
{
    internal class SprCommand : GenericCommand<SprResponse>
    {
        private static readonly ILogger s_logger = EbicsLogging.CreateLogger<SprCommand>();
        private readonly byte[] _transactionKey;

        internal SprParams Params { private get; set; }
        internal override string OrderType => "SPR";
        internal override string OrderAttribute => "UZHNN";
        internal override TransactionType TransactionType => TransactionType.Upload;
        internal override IList<XmlDocument> Requests => null;
        internal override XmlDocument InitRequest => CreateInitRequest();
        internal override XmlDocument ReceiptRequest => null;

        public SprCommand()
        {
            _transactionKey = CryptoUtils.GetTransactionKey();
            s_logger.LogDebug("Transaction Key: {key}", CryptoUtils.Print(_transactionKey));
        }

        private XmlDocument CreateInitRequest()
        {
            using (new MethodLogger(s_logger))
            {
                try
                {
                    // TODO: distinguish between A005 and A006
                    // netcore currentyl doesn't support PSS padding in version 2.x
                    var signedData =
                        Config.User.SignKeys.PrivateKey.SignData(Encoding.ASCII.GetBytes(" "), HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);

                    var userSigData = new UserSignatureData
                    {
                        Namespaces = Namespaces,
                        OrderSignatureData = new OrderSignatureData
                        {
                            Namespaces = Namespaces,
                            PartnerId = Config.User.PartnerId,
                            UserId = Config.User.UserId,
                            SignatureValue = signedData,
                            SignKeys = Config.User.SignKeys
                        }
                    };

                    s_logger.LogDebug("User signature data:\n{data}", userSigData.ToString());

                    var userSigDataComp =
                        Compress(
                            Encoding.UTF8.GetBytes(userSigData.Serialize().ToString(SaveOptions.DisableFormatting)));
                    var userSigDataEnc = EncryptAes(userSigDataComp, _transactionKey);

                    XNamespace nsEbics = Namespaces.Ebics;

                    var initReq = new EbicsRequest
                    {
                        Namespaces = Namespaces,
                        Version = Config.Version,
                        Revision = Config.Revision,
                        StaticHeader = new StaticHeader
                        {
                            Namespaces = Namespaces,
                            HostId = Config.User.HostId,
                            Nonce = CryptoUtils.GetNonce(),
                            Timestamp = CryptoUtils.GetUtcTimeNow(),
                            PartnerId = Config.User.PartnerId,
                            UserId = Config.User.UserId,
                            NumSegments = 0,
                            OrderDetails = new OrderDetails
                            {
                                Namespaces = Namespaces,
                                OrderType = OrderType,
                                OrderAttribute = OrderAttribute,
                                StandardOrderParams = new EmptyOrderParams(),
                            },
                            BankPubKeyDigests = new BankPubKeyDigests
                            {
                                Namespaces = Namespaces,
                                DigestAlgorithm = s_digestAlg,
                                Bank = Config.Bank
                            }
                        },
                        MutableHeader = new MutableHeader
                        {
                            Namespaces = Namespaces,
                            TransactionPhase = "Initialisation"
                        },
                        Body = new Body
                        {
                            Namespaces = Namespaces,
                            DataTransfer = new DataTransfer
                            {
                                Namespaces = Namespaces,
                                DataEncryptionInfo = new DataEncryptionInfo
                                {
                                    Namespaces = Namespaces,
                                    EncryptionPubKeyDigest = new EncryptionPubKeyDigest
                                    {
                                        Namespaces = Namespaces,
                                        Bank = Config.Bank,
                                        DigestAlgorithm = s_digestAlg
                                    },
                                    TransactionKey = Convert.ToBase64String(EncryptRsa(_transactionKey))
                                },
                                SignatureData = new SignatureData
                                {
                                    Namespaces = Namespaces
                                }
                            }
                        }
                    };

                    var xmlInitReq = initReq.Serialize();
                    xmlInitReq.Descendants(nsEbics + XmlNames.SignatureData).FirstOrDefault()
                        .Add(Convert.ToBase64String(userSigDataEnc));
                    return SignXml(xmlInitReq.ToXmlDocument(), null, null);
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