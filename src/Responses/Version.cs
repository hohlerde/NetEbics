using System.Collections.Generic;

namespace NetEbics.Responses
{
    public class Version
    {
        public IEnumerable<string> Protocols { get; set; }        
        public IEnumerable<string> Authentications { get; set; }
        public IEnumerable<string> Encryptions { get; set; }
        public IEnumerable<string> Signatures { get; set; }
    }
}