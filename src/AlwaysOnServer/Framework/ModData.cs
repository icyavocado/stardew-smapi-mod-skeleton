using System;
using System.Reflection;
using System.Collections.Concurrent;

namespace Always_On_Server.Framework
{
    public class ModData
    {
        // properties...
        public int FarmingLevel { get; set; }
        public int MiningLevel { get; set; }
        public int ForagingLevel { get; set; }
        public int FishingLevel { get; set; }
        public int CombatLevel { get; set; }
        public int FarmingExperience { get; set; }
        public int MiningExperience { get; set; }
        public int ForagingExperience { get; set; }
        public int FishingExperience { get; set; }
        public int CombatExperience { get; set; }

        // optional cache for performance
        private static readonly ConcurrentDictionary<string, PropertyInfo?> _propCache =
            new(StringComparer.OrdinalIgnoreCase);

        private PropertyInfo? FindProp(string name) =>
            _propCache.GetOrAdd(name, n => typeof(ModData).GetProperty(n));

        public void Set(string propertyName, int value = 0)
        {
            var prop = FindProp(propertyName);
            if (prop == null)
                throw new ArgumentException($"Property '{propertyName}' does not exist.");
            if (!prop.CanWrite)
                throw new ArgumentException($"Property '{propertyName}' is read-only.");
            if (prop.PropertyType != typeof(int))
                throw new ArgumentException($"Property '{propertyName}' is not an int.");

            prop.SetValue(this, value);
        }

        public int Get(string propertyName)
        {
            var prop = FindProp(propertyName);
            if (prop == null)
                throw new ArgumentException($"Property '{propertyName}' does not exist.");
            if (!prop.CanRead)
                throw new ArgumentException($"Property '{propertyName}' is write-only.");
            if (prop.PropertyType != typeof(int))
                throw new ArgumentException($"Property '{propertyName}' is not an int.");

            var raw = prop.GetValue(this);
            return raw is int i ? i : throw new InvalidOperationException($"Property '{propertyName}' value is not an int.");
        }
    }
}
