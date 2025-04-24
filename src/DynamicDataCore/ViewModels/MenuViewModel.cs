using System.Collections.Generic;

namespace DynamicDataCore.ViewModels
{
    public class MenuViewModel
    {
        public List<string> DbContextNames { get; set; } = new List<string>();
        public List<string> DbSetNames { get; set; } = new List<string>();
    }
}
