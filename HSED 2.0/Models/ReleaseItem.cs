using System;

namespace HSED_2._0.Models
{
    public class ReleaseItem
    {
        public string Tag { get; set; }
        public bool IsLatest { get; set; }

        public DateTime Published { get; set; }
    }
}
