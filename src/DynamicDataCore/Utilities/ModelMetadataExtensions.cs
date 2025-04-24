using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DynamicDataCore.Utilities
{
    public static class ModelMetadataExtensions
    {
        public static string GetDisplayFormat(this ModelMetadata metadata)
        {
            if (metadata == null) 
            {
                return null;
            }

            return metadata.AdditionalValues.ContainsKey("DisplayFormat")
                ? metadata.AdditionalValues["DisplayFormat"].ToString()
                : metadata.DisplayFormatString ?? null;
        }
    }
}