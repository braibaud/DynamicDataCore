using System.Collections.Generic;

namespace DynamicDataCore.ViewModels
{
    public class DataDeleteViewModel
    {
        public string DbSetName { get; set; }
        public object Object { get; set; }
        public IEnumerable<KeyValuePair<string, object>> PrimaryKeys { get; set; }
    }
}