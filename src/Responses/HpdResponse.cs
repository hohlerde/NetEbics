namespace NetEbics.Responses
{
    public class HpdResponse : Response
    {
        public AccessParams AccessParams { get; internal set; }
        public ProtocolParams ProtocolParams { get; internal set; }
    }
}