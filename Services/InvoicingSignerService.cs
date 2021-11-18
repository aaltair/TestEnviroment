using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using API.Dtos;
using API.Dtos.Exceptions;
using API.Dtos.Options;
using API.Dtos.Validations;
using API.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ess;

namespace API.Services
{
    public class InvoicingSignerService:IInvoicingSignerService
    {
        private readonly ILogger<InvoicingSignerService> _logger;
        private readonly ESPOptions _espOptions;

        public InvoicingSignerService(ILogger<InvoicingSignerService> logger,IOptions<ESPOptions> options)
        {
            _logger = logger;
            _espOptions = options.Value;
        }
        public async Task<string> InvoicingSigner(InvoicingSignerDto invoicingSignerDto)
        {
            var validator = new InvoicingSignerValidator();
            if (!(await validator.ValidateAsync(invoicingSignerDto)).IsValid) throw new AppException("Invalid input");
            String cades = "";
            String SourceDocumentJson = invoicingSignerDto.JsonString;
            JObject? request = JsonConvert.DeserializeObject<JObject>(SourceDocumentJson, new JsonSerializerSettings()
            {
                FloatFormatHandling = FloatFormatHandling.String,
                FloatParseHandling = FloatParseHandling.Decimal,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateParseHandling = DateParseHandling.None
            });

            //Start serialize
            String canonicalString = Serialize(request);
            if (request["documentTypeVersion"].Value<string>() == "0.9")
            {
                cades = "ANY";
            }
            else
            {
                cades = SignWithCMS(canonicalString,invoicingSignerDto.Token);
            }
            JObject signaturesObject = new JObject(
                new JProperty("signatureType", "I"),
                new JProperty("value", cades));
            JArray signaturesArray = new JArray();
            signaturesArray.Add(signaturesObject);
            request.Add("signatures", signaturesArray);
            String fullSignedDocument = "{\"documents\":[" + request.ToString() + "]}";

            return fullSignedDocument;

        }
        
        
        
        private byte[] Hash(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var output = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                return output;
            }
        }

        private byte[] HashBytes(byte[] input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var output = sha.ComputeHash(input);
                return output;
            }
        }
    
        public string SignWithCMS(String serializedJson,string tokenPin)
        {
            byte[] data = Encoding.UTF8.GetBytes(serializedJson);
            Pkcs11InteropFactories factories = new Pkcs11InteropFactories();
            using (IPkcs11Library pkcs11Library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, _espOptions.DllPath, AppType.MultiThreaded))
            {
                ISlot slot = pkcs11Library.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault();

                if (slot is null)
                {
                    throw new AppException("No slots found");
                }

                ITokenInfo tokenInfo = slot.GetTokenInfo();

                ISlotInfo slotInfo = slot.GetSlotInfo();


                using (var session = slot.OpenSession(SessionType.ReadWrite))
                {

                    session.Login(CKU.CKU_USER, Encoding.UTF8.GetBytes(tokenPin));

                    var certificateSearchAttributes = new List<IObjectAttribute>()
                    {
                        session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
                        session.Factories.ObjectAttributeFactory.Create(CKA.CKA_TOKEN, true),
                        session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509)
                    };

                    IObjectHandle certificate = session.FindAllObjects(certificateSearchAttributes).FirstOrDefault();

                    if (certificate is null)
                    {
                        throw new AppException("Certificate not found");
                    }

                    X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.MaxAllowed);

                // find cert by thumbprint
                var foundCerts = store.Certificates.Find(X509FindType.FindByIssuerName, "Egypt Trust Sealing CA", false);

                //var foundCerts = store.Certificates.Find(X509FindType.FindBySerialNumber, "2b1cdda84ace68813284519b5fb540c2", true);

                

                    if (foundCerts.Count == 0)
                        throw new AppException("no device detected");

                    var certForSigning = foundCerts[0];
                    store.Close();


                    ContentInfo content = new ContentInfo(new Oid("1.2.840.113549.1.7.5"), data);


                    SignedCms cms = new SignedCms(content, true);

                    EssCertIDv2 bouncyCertificate = new EssCertIDv2(new Org.BouncyCastle.Asn1.X509.AlgorithmIdentifier(new DerObjectIdentifier("1.2.840.113549.1.9.16.2.47")), this.HashBytes(certForSigning.RawData));

                    SigningCertificateV2 signerCertificateV2 = new SigningCertificateV2(new EssCertIDv2[] { bouncyCertificate });


                    CmsSigner signer = new CmsSigner(certForSigning);

                    signer.DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1");



                    signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
                    signer.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), signerCertificateV2.GetEncoded()));


                    cms.ComputeSignature(signer);

                    var output = cms.Encode();

                    return Convert.ToBase64String(output);
                }
            }
        }

        public string Serialize(JObject request)
    {
        return SerializeToken(request);
    }

        private string SerializeToken(JToken request)
    {
        string serialized = "";
        if (request.Parent is null)
        {
            SerializeToken(request.First);
        }
        else
        {
            if (request.Type == JTokenType.Property)
            {
                string name = ((JProperty)request).Name.ToUpper();
                serialized += "\"" + name + "\"";
                foreach (var property in request)
                {
                    if (property.Type == JTokenType.Object)
                    {
                        serialized += SerializeToken(property);
                    }
                    if (property.Type == JTokenType.Boolean || property.Type == JTokenType.Integer || property.Type == JTokenType.Float || property.Type == JTokenType.Date)
                    {
                        serialized += "\"" + property.Value<string>() + "\"";
                    }
                    if(property.Type == JTokenType.String)
                    {
                        serialized +=  JsonConvert.ToString(property.Value<string>()) ;
                    }
                    if (property.Type == JTokenType.Array)
                    {
                        foreach (var item in property.Children())
                        {
                            serialized += "\"" + ((JProperty)request).Name.ToUpper() + "\"";
                            serialized += SerializeToken(item);
                        }
                    }
                }
            }
        }
        if (request.Type == JTokenType.Object)
        {
            foreach (var property in request.Children())
            {

                if (property.Type == JTokenType.Object || property.Type == JTokenType.Property)
                {
                    serialized += SerializeToken(property);
                }
            }
        }

        return serialized;
    }


        public void ListCertificates()
    {
       
        X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.MaxAllowed);
        X509Certificate2Collection collection = (X509Certificate2Collection)store.Certificates;
        X509Certificate2Collection fcollection = (X509Certificate2Collection)collection.Find(X509FindType.FindBySerialNumber, "2b1cdda84ace68813284519b5fb540c2", true);
        foreach (X509Certificate2 x509 in fcollection)
        {
            try
            {
               
                byte[] rawdata = x509.RawData;
               _logger.LogInformation("Content Type: {0}{1}", X509Certificate2.GetCertContentType(rawdata), Environment.NewLine);
               _logger.LogInformation("Friendly Name: {0}{1}", x509.FriendlyName, Environment.NewLine);
               _logger.LogInformation("Certificate Verified?: {0}{1}", x509.Verify(), Environment.NewLine);
               _logger.LogInformation("Simple Name: {0}{1}", x509.GetNameInfo(X509NameType.SimpleName, true), Environment.NewLine);
               _logger.LogInformation("Signature Algorithm: {0}{1}", x509.SignatureAlgorithm.FriendlyName, Environment.NewLine);
               _logger.LogInformation("Public Key: {0}{1}", x509.PublicKey.Key.ToXmlString(false), Environment.NewLine);
               _logger.LogInformation("Certificate Archived?: {0}{1}", x509.Archived, Environment.NewLine);
               _logger.LogInformation("Length of Raw Data: {0}{1}", x509.RawData.Length, Environment.NewLine);               
                x509.Reset();
            }
            catch (CryptographicException ex)
            {
               _logger.LogInformation("Information could not be written out for this certificate.");
                throw ex;
            }
        }
        store.Close();       
    }

    }
}