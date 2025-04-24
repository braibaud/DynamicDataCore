using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace DynamicDataCore
{
    public class JsonLocalizer : IStringLocalizer<JsonLocalizer>
    {
        public Dictionary<string, Dictionary<string, string>> translations = new();
        private readonly IHttpContextAccessor httpContextAccessor;

        public JsonLocalizer(IHttpContextAccessor httpContextAccessor)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string embeddedResource in assembly.GetManifestResourceNames().Where(e => e.EndsWith(".json")))
            {
                StreamReader textStreamReader = new StreamReader(assembly.GetManifestResourceStream(embeddedResource));
                translations.Add(embeddedResource, JsonSerializer.Deserialize<Dictionary<string, string>>(textStreamReader.ReadToEnd()));
            }

            this.httpContextAccessor = httpContextAccessor;
        }

        public LocalizedString this[string name] => GetAllStrings(false).FirstOrDefault(s => s.Name == name) ?? new LocalizedString(name, name);

        public LocalizedString this[string name, params object[] arguments] => GetAllStrings(false).FirstOrDefault(s => s.Name == name) ?? new LocalizedString(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            Microsoft.Extensions.Primitives.StringValues cultureKeys = httpContextAccessor.HttpContext.Request.Headers["Accept-Language"];

            foreach(string culture in cultureKeys)
            {
                string fileName = $"{culture}.json";
                string key = translations.Keys.FirstOrDefault(k => k.EndsWith(fileName));
                if (key != null)
                {
                    return translations[key].Select(s => new LocalizedString(s.Key, s.Value));
                }
            }

            // fall back to en-US
            return translations[translations.Keys.First(k => k.EndsWith("en-US.json"))].Select(s => new LocalizedString(s.Key, s.Value));
        }
    }
}
