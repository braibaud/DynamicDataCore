using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicDataCore.Utilities
{
    public static class TypeConverterUtility
    {
        public static string NullValueString => "_null_";

        public static bool EqualsIgnoringCase(string left, string right)
        {
            return String.Compare(left, right, StringComparison.OrdinalIgnoreCase) == 0;
        }

        public static List<dynamic> BuildViewBagProperties(IDictionary<string, string> primaryKeys)
        {
            if (primaryKeys == null || !primaryKeys.Any())
            {
                return new List<dynamic>();
            }

            return primaryKeys
                .Select(
                    pk => (dynamic)new
                    {
                        Name = pk.Key,
                        Value = pk.Value
                    })
                .ToList();
        }

        public static object ConvertToType(string value, Type targetType)
        {
            if (EqualsIgnoringCase(value, TypeConverterUtility.NullValueString))
            {
                if (targetType.IsClass)
                {
                    return Convert.ChangeType(null, targetType);
                }

                return null!;
            }

            if (targetType == typeof(Guid))
            {
                // Handle Guid conversion
                if (Guid.TryParse(value, out Guid guidValue))
                {
                    return guidValue;
                }

                throw new InvalidCastException($"Cannot convert '{value}' to Guid.");
            }

            if (targetType.IsEnum)
            {
                // Handle Enum conversion
                if (Enum.TryParse(targetType, value, true, out object enumValue))
                {
                    return enumValue;
                }

                throw new InvalidCastException($"Cannot convert '{value}' to Enum of type {targetType.Name}.");
            }

            if (targetType == typeof(DateTime))
            {
                // Handle DateTime conversion
                if (DateTime.TryParse(value, out DateTime dateTimeValue))
                {
                    return dateTimeValue;
                }

                throw new InvalidCastException($"Cannot convert '{value}' to DateTime.");
            }

            if (targetType == typeof(bool))
            {
                // Handle Boolean conversion
                if (bool.TryParse(value, out bool boolValue))
                {
                    return boolValue;
                }
                
                throw new InvalidCastException($"Cannot convert '{value}' to Boolean.");
            }

            // Handle other types using Convert.ChangeType
            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException($"Cannot convert '{value}' to {targetType.Name}.", ex);
            }
        }

        public static object ConvertObjectToType(object value, Type targetType)
        {
            if (value == null)
            {
                if (targetType.IsClass)
                {
                    return null!;
                }

                throw new InvalidCastException($"Cannot convert null to {targetType.Name}.");
            }

            if (value is string stringValue)
            {
                // Redirect to the existing ConvertToType method for strings
                return ConvertToType(stringValue, targetType);
            }

            if (targetType.IsAssignableFrom(value.GetType()))
            {
                // If the value is already of the target type, return it directly
                return value;
            }

            try
            {
                // Handle other types using Convert.ChangeType
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException($"Cannot convert '{value}' of type {value.GetType().Name} to {targetType.Name}.", ex);
            }
        }

        public static T Coalesce<T>(params T[] values)
        {
            foreach (var value in values)
            {
                if (value != null)
                {
                    return value;
                }
            }
            
            return default!;
        }
    }
}