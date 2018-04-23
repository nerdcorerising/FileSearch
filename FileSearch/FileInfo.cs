using System;
namespace FileSearch
{
    public class FileInfo
    {
        public string Path { get; set; }
        public DateTime LastModified { get; set; }
        public string Contents { get; set; }
    }
}
