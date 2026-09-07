using System;
using System.Runtime.Serialization;

namespace LogAnalyzer
{
    [DataContract]
    public class RecentSearchItem
    {
        [DataMember(Name = "text")]
        public string Text { get; set; }

        [DataMember(Name = "lastUsed")]
        public DateTime LastUsed { get; set; }
    }
}
