using System;

namespace HSED_2._0.Models
{
    public class GitHubRelease
    {
        public string tag_name { get; set; }
        public string name { get; set; }
        public bool prerelease { get; set; }
        public DateTime published_at { get; set; }
    }
}
