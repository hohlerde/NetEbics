/*
 * NetEbics -- .NET Core EBICS Client Library
 * (c) Copyright 2018 Bjoern Kuensting
 *
 * This file is subject to the terms and conditions defined in
 * file 'LICENSE.txt', which is part of this source code package.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NetEbics.Config;

namespace NetEbics.Xml
{
    internal class CustomSignedXml
    {
        private XmlDocument _doc;
        private byte[] _digestValue;
        private byte[] _signatureValue;
        private readonly Encoding _docEncoding = Encoding.UTF8;

        private const string _digestAlg = "http://www.w3.org/2001/04/xmlenc#sha256";
        private const string _signAlg = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        private const string _canonAlg = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
        private const string _defaultNsKey = "DEFAULT";

        public const string DefaultReferenceUri = "#xpointer(//*[@authenticate='true'])";

        public string ReferenceUri { private get; set; }
        public RSA SignatureKey { private get; set; }
        public RSASignaturePadding SignaturePadding { private get; set; }

        public string CanonicalizationAlgorithm { private get; set; }
        public string DigestAlgorithm { private get; set; }
        public string SignatureAlgorithm { private get; set; }

        public byte[] DigestValue => _digestValue;
        public byte[] SignatureValue => _signatureValue;

        public XmlNamespaceManager NamespaceManager { private get; set; }
        public NamespaceConfig Namespaces { private get; set; }

        public CustomSignedXml(XmlDocument doc)
        {
            _doc = doc;
            if (_doc.FirstChild.NodeType == XmlNodeType.XmlDeclaration)
            {
                var enc = ((XmlDeclaration) _doc.FirstChild).Encoding;
                try
                {
                    _docEncoding = Encoding.GetEncoding(enc);
                }
                catch (ArgumentException)
                {
                }
            }
        }

        private byte[] Digest(string xml)
        {
            using (var hash = SHA256.Create())
            {
                return hash.ComputeHash(_docEncoding.GetBytes(xml));
            }
        }

        private string Canonicalize(XmlDocument doc, IList<XmlNode> nsl = null)
        {
            var transform = new XmlDsigC14NTransform {Algorithm = CanonicalizationAlgorithm};
            var sb = new StringBuilder();

            if (nsl != null && nsl.Count > 0)
            {
                foreach (XmlNode attrNode in nsl)
                {
                    if (!doc.DocumentElement.HasAttribute(attrNode.Name))
                    {
                        doc.DocumentElement.SetAttribute(attrNode.Name, attrNode.Value);
                    }
                }
            }

            transform.LoadInput(doc);
            using (var stream = (Stream) transform.GetOutput(typeof(Stream)))
            {
                using (var reader = new StreamReader(stream))
                {
                    sb.Append(reader.ReadToEnd());
                }
            }

            return sb.ToString();
        }

        private byte[] CanonicalizeAndDigest(IEnumerable nodes, IList<XmlNode> nsl = null)
        {
            var sb = new StringBuilder();
            foreach (XmlNode node in nodes)
            {
                var tmpDoc = new XmlDocument();
                tmpDoc.AppendChild(tmpDoc.ImportNode(node, true));
                sb.Append(Canonicalize(tmpDoc, nsl));
            }

            return Digest(sb.ToString());
        }

        private XDocument CreateDoc(byte[] digest, string dsPrefix = null)
        {
            XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";

            var signedInfo = new XElement(ds + XmlNames.SignedInfo);
            if (!string.IsNullOrEmpty(dsPrefix))
            {
                signedInfo.Add(new XAttribute(XNamespace.Xmlns + dsPrefix, ds));
            }

            signedInfo.Add(
                new XElement(ds + XmlNames.CanonicalizationMethod,
                    new XAttribute(XmlNames.Algorithm, CanonicalizationAlgorithm)
                ),
                new XElement(ds + XmlNames.SignatureMethod,
                    new XAttribute(XmlNames.Algorithm, SignatureAlgorithm)
                ),
                new XElement(ds + XmlNames.Reference,
                    new XAttribute(XmlNames.URI, ReferenceUri),
                    new XElement(ds + XmlNames.Transforms,
                        new XElement(ds + XmlNames.Transform,
                            new XAttribute(XmlNames.Algorithm, CanonicalizationAlgorithm)
                        )
                    ),
                    new XElement(ds + XmlNames.DigestMethod,
                        new XAttribute(XmlNames.Algorithm, DigestAlgorithm)
                    ),
                    new XElement(ds + XmlNames.DigestValue, Convert.ToBase64String(digest))
                )
            );

            return new XDocument(signedInfo);
        }

        public XmlElement GetXml()
        {
            XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
            var signature = new XDocument(
                new XElement("Signature",
                    CreateDoc(_digestValue).Elements()
                )
            );
            signature.Descendants("Signature").FirstOrDefault()?
                .Add(new XElement(ds + XmlNames.SignatureValue, Convert.ToBase64String(_signatureValue)));
            var doc = signature.ToXmlDocument(true);
            return (XmlElement) doc.FirstChild;
        }

        public void ComputeSignature()
        {
            if (DigestAlgorithm != _digestAlg)
            {
                throw new CryptographicException($"Digest algorithm not supported. Use: {_digestAlg}");
            }

            if (SignatureAlgorithm != _signAlg)
            {
                throw new CryptographicException($"Signature algorithm not supported. Use: {_signAlg}");
            }

            if (CanonicalizationAlgorithm != _canonAlg)
            {
                throw new CryptographicException($"Canonicalization algorithm not supported. Use: {_canonAlg}");
            }

            if (SignatureKey == null)
            {
                throw new CryptographicException($"{nameof(SignatureKey)} is null");
            }

            if (ReferenceUri == null)
            {
                throw new CryptographicException($"{nameof(ReferenceUri)} is null");
            }

            var _ref = ReferenceUri;
            if (ReferenceUri.StartsWith("#xpointer("))
            {
                var customXPath = ReferenceUri.TrimEnd(')');
                _ref = customXPath.Substring(customXPath.IndexOf('(') + 1);
            }

            var nodes = NamespaceManager == null ? _doc.SelectNodes(_ref) : _doc.SelectNodes(_ref, NamespaceManager);

            if (nodes.Count == 0)
            {
                throw new CryptographicException("No references found");
            }

            _digestValue = CanonicalizeAndDigest(nodes);

            var signedInfo = CreateDoc(_digestValue);

            nodes = signedInfo.ToXmlDocument().SelectNodes("*");
            var signedInfoDigest = CanonicalizeAndDigest(nodes);
            _signatureValue =
                SignatureKey.SignHash(signedInfoDigest, HashAlgorithmName.SHA256, SignaturePadding);
        }

        private IList<XmlNode> GetNamespaceList(XmlDocument doc)
        {
            var xmlNameSpaceList = doc.SelectNodes(@"//namespace::*[not(. = ../../namespace::*)]");
            var myList = new List<XmlNode>();

            foreach (XmlNode node in xmlNameSpaceList)
            {
                if (node.Name.StartsWith("xmlns") && node.Name != "xmlns:xml")
                {
                    myList.Add(node);
                }
            }

            return myList;
        }

        private XmlNamespaceManager CreateNamespaceManager(XmlDocument doc, IEnumerable<XmlNode> namespaces)
        {
            var nm = new XmlNamespaceManager(doc.NameTable);

            foreach (var node in namespaces)
            {
                if (nm.HasNamespace(node.LocalName)) continue;

                if (node.Name == "xmlns")
                {
                    nm.AddNamespace(string.Empty, node.Value);
                    nm.AddNamespace(_defaultNsKey, node.Value);
                    continue;
                }

                nm.AddNamespace(node.LocalName, node.Value);
            }

            return nm;
        }

        public bool VerifySignature()
        {
            var xmlNameSpaceList = GetNamespaceList(_doc);
            var nm = CreateNamespaceManager(_doc, xmlNameSpaceList);
            var xph = new XPathHelper(XDocument.Parse(_doc.OuterXml, LoadOptions.PreserveWhitespace), Namespaces);

            var refNodes = xph.GetAuthSignatureReferences().ToList();
            if (refNodes.Count != 1)
            {
                return false;
            }

            var refUri = refNodes[0].Attribute("URI")?.Value;
            if (refUri == null)
            {
                return false;
            }

            if (refUri.StartsWith("#xpointer("))
            {
                var customXPath = refUri.TrimEnd(')');
                refUri = customXPath.Substring(customXPath.IndexOf('(') + 1);
            }

            var nodes = _doc.SelectNodes(refUri, nm);
            if (nodes.Count == 0)
            {
                return false;
            }

            var dsigPrefix = nm.LookupPrefix(Namespaces.XmlDsig);
            var ebicsPrefix = nm.LookupPrefix(Namespaces.Ebics);

            if (dsigPrefix == "") dsigPrefix = _defaultNsKey;
            if (ebicsPrefix == "") ebicsPrefix = _defaultNsKey;

            var myDigestValue = CanonicalizeAndDigest(nodes, xmlNameSpaceList);
            var myB64DigestValue = Convert.ToBase64String(myDigestValue);
            var b64Digest = xph.GetAuthSignatureDigestValue()?.Value.Trim();

            if (b64Digest != myB64DigestValue)
            {
                return false;
            }

            var signedInfoNode =
                _doc.SelectSingleNode($"/*/{ebicsPrefix}:{XmlNames.AuthSignature}/{dsigPrefix}:{XmlNames.SignedInfo}",
                    nm);
            var signedInfoDoc = new XmlDocument();
            signedInfoDoc.AppendChild(signedInfoDoc.ImportNode(signedInfoNode, true));

            var mySignedInfoDigest = Digest(Canonicalize(signedInfoDoc, xmlNameSpaceList));

            var b64Signature = xph.GetAuthSignatureValue()?.Value.Trim();
            var signature = Convert.FromBase64String(b64Signature);

            return
                SignatureKey.VerifyHash(mySignedInfoDigest, signature, HashAlgorithmName.SHA256,
                    SignaturePadding);
        }
    }
}