namespace NetEbics.Responses
{
    public class ProtocolParams
    {
        public Version Version { get; internal set; }
        public bool RecoverySupported { get; internal set; } = true;
        public bool PreValidationSupported { get; internal set; } = true;
        public bool X509DataSupported { get; internal set; } = true;
        public bool X509DataPersistent { get; internal set; } = false;
        public bool ClientDataDownloadSupported { get; internal set; } = true;
        public bool DownloadableOrderDataSupported { get; internal set; } = true;
    }
}