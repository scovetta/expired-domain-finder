using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpiredDomainFinder
{
    internal class Row
    {
        public string Package { get; set; }
        public string Domain { get; set; }
        public string Expiration { get; set; }
    }
}