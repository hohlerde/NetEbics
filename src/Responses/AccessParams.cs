using System;
using System.Collections.Generic;

namespace NetEbics.Responses
{
    public class AccessParams
    {
        public IEnumerable<Url> Urls { get; internal set; }
        public string Institute { get; internal set; }
        public string HostId { get; internal set; }
    }
}