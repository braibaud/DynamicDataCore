using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DynamicDataCore.Utilities
{
    public static class ModelMetadataExtensions
    {
        public static string GetDisplayFormat(this ModelMetadata metadata)
        {
            return TypeConverterUtility.Coalesce(
                metadata?.AdditionalValues["DisplayFormat"]?.ToString(),
                metadata?.DisplayFormatString);
        }

        public static string GetDisplayName(this ModelMetadata metadata)
        {
            return TypeConverterUtility.Coalesce(
                metadata?.AdditionalValues["DisplayName"]?.ToString(),
                metadata?.DisplayName,
                metadata?.PropertyName,
                metadata?.Name);
        }
    }
}