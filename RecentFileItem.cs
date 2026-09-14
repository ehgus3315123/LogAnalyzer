using System;
using System.Runtime.Serialization;

namespace LogViewer
{
    [DataContract]
    public class RecentFileItem
    {
        [DataMember(Name = "path")]
        public string FilePath { get; set; }

        [DataMember(Name = "lastOpened")]
        public DateTime LastOpened { get; set; }
    }
}
