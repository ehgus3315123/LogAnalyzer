using System;
using System.Runtime.Serialization;

namespace LogViewer
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
