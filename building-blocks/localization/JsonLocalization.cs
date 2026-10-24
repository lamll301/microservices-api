using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BuildingBlocks.Localization
{
    public class SharedResource { }

    public class AutoGenJsonLocalizer : IStringLocalizer<SharedResource>
    {
        private readonly string _jsonFilePath;
        private static readonly ConcurrentDictionary<string, string> _cache = new();
        private static readonly SemaphoreSlim _semaphore = new(1, 1);
        private const string TargetFolder = "src";
        private const string LocaleFileName = "en.json";
        private static readonly string[] PathSegments = { "BuildingBlocks", "Localization", "Locales" };

        public AutoGenJsonLocalizer()
        {
            _jsonFilePath = ResolveJsonFilePath();
            LoadExistingKeys();
        }
        
        public LocalizedString this[string name]
        {
            get
            {
                _ = RegisterNewKeyAsync(name);
                return new LocalizedString(name, name);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                _ = RegisterNewKeyAsync(name);
                return new LocalizedString(name, string.Format(name, arguments));
            }
        }

        private string ResolveJsonFilePath()
        {
            var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (currentDir != null && currentDir.Name != TargetFolder && currentDir.Name != "server")
            {
                currentDir = currentDir.Parent;
            }
            if (currentDir == null) return Path.Combine(AppContext.BaseDirectory, LocaleFileName);
            var baseSrcPath = currentDir.Name == TargetFolder ? currentDir.FullName : Path.Combine(currentDir.FullName, TargetFolder);
            var fullFolderDirectory = Path.Combine(baseSrcPath, Path.Combine(PathSegments));
            if (!Directory.Exists(fullFolderDirectory))
                Directory.CreateDirectory(fullFolderDirectory);
            return Path.Combine(fullFolderDirectory, LocaleFileName);
        }

        private void LoadExistingKeys()
        {
            if (!File.Exists(_jsonFilePath)) return;
            try
            {
                var jsonString = File.ReadAllText(_jsonFilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _cache.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
            catch
            { 
            
            }
        }

        private async Task RegisterNewKeyAsync(string key)
        {
            if (string.IsNullOrEmpty(key) || _cache.ContainsKey(key)) return;

            _cache.TryAdd(key, key);
            await _semaphore.WaitAsync();

            try
            {
                Dictionary<string, string> dict = new();
                if (File.Exists(_jsonFilePath))
                {
                    try
                    {
                        var jsonString = await File.ReadAllTextAsync(_jsonFilePath);
                        dict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString) ?? new();
                    }
                    catch { dict = new(); }
                }
                if (!dict.ContainsKey(key))
                {
                    dict.Add(key, key);
                    var options = new JsonSerializerOptions { WriteIndented = true };

                    await File.WriteAllTextAsync(_jsonFilePath, JsonSerializer.Serialize(dict, options));
                }
            }
            catch
            {
                _cache.TryRemove(key, out _);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
    }

    public static class JsonLocalizationExtensions
    {
        public static IServiceCollection AddCustomJsonLocalization(this IServiceCollection services)
        {
            services.AddSingleton<IStringLocalizer<SharedResource>, AutoGenJsonLocalizer>();
            return services;
        }

        public static IApplicationBuilder UseCustomJsonLocalization(this IApplicationBuilder app)
        {
            var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("vi") };
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            });
            return app;
        }
    }
}
