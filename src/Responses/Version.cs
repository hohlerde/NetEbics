using System.Collections.Generic;

namespace NetEbics.Responses
{
    public class Version
    {
        public IEnumerable<string> Protocols { get; internal set; }
        public IEnumerable<string> Authentications { get; internal set; }
        public IEnumerable<string> Encryptions { get; internal set; }
        public IEnumerable<string> Signatures { get; internal set; }
    }
}