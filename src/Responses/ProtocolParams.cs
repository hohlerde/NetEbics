namespace NetEbics.Responses
{
    public class ProtocolParams
    {
        public Version Version { get; set; }
        public bool RecoverySupported { get; set; } = true;
        public bool PreValidationSupported { get; set; } = true;
        public bool X509DataSupported { get; set; } = true;
        public bool X509DataPersistent { get; set; } = false;
        public bool ClientDataDownloadSupported { get; set; } = true;
        public bool DownloadableOrderDataSupported { get; set; } = true;
    }
}