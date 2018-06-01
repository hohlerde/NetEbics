using System;
using System.Collections.Generic;

namespace NetEbics.Responses
{
    public class AccessParams
    {
        public IEnumerable<Url> Urls { get; set; }
        public string Institute { get; set; }
        public string HostId { get; set; }
    }
}