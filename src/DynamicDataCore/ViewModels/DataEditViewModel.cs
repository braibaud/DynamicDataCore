using System.Collections.Generic;

namespace DynamicDataCore.ViewModels
{
    public class DataEditViewModel
    {
        public string DbSetName { get; set; }
        public IDictionary<string, string> FormData { get; set; }
        public IEnumerable<KeyValuePair<string, object>> PrimaryKeys { get; set; }
    }
}