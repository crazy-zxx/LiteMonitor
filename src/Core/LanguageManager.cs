using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteMonitor.src.Core
{
    public static class LanguageManager
    {
        public static string CurrentLang { get; private set; } = "zh";
        private static Dictionary<string, string> _texts = new();
        
        // ★★★ 1. 新增：用户自定义覆盖字典 ★★★
        private static Dictionary<string, string> _overrides = new();

        private static string LangDir
        {
            get
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "resources/lang");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static void Load(string langCode)
        {
            try
            {
                var path = Path.Combine(LangDir, $"{langCode}.json");
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[LanguageManager] Missing lang file: {langCode}.json");
                    return;
                }
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                _texts = Flatten(doc.RootElement);
                CurrentLang = langCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LanguageManager] Load failed: {ex.Message}");
            }
        }

        // ★★★ 2. 新增：注入/清除覆盖的方法 ★★★
        public static void SetOverride(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(value)) 
            {
                if (_overrides.ContainsKey(key)) _overrides.Remove(key);
            }
            else 
            {
                _overrides[key] = value;
            }
        }

        public static void ClearOverrides() => _overrides.Clear();

        public static string T(string key)
        {
            // ★★★ 3. 核心修改：优先检查覆盖值 ★★★
            if (_overrides.TryGetValue(key, out var overrideVal)) return overrideVal;

            // 原有逻辑
            if (_texts.TryGetValue(key, out var val)) return val;
            int dot = key.IndexOf('.');
            return dot >= 0 ? key[(dot + 1)..] : key;
        }

        private static Dictionary<string, string> Flatten(JsonElement element, string prefix = "")
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in element.EnumerateObject())
            {
                string fullKey = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in Flatten(prop.Value, fullKey))
                        dict[kv.Key] = kv.Value;
                }
                else
                {
                    dict[fullKey] = prop.Value.GetString() ?? "";
                }
            }
            return dict;
        }
    }
}