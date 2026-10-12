using System;
using System.IO;
using System.Runtime.Serialization;

namespace KidGuard {
    // Admin\settings.json: preferences that are not part of the protection policy.
    [DataContract] public class Settings {
        // "en" or "bg"; absent means the Windows display language.
        [DataMember(EmitDefaultValue=false)] public string language;
        // The child's helper next to the clock (time left, asking for sites and time) is on unless turned off.
        [DataMember] public bool hideChildHelper;

        public string Language { get { return language == "bg" || language == "en" ? language : L.Default; } }
        static string PathOf(string admin) { return Path.Combine(admin, "settings.json"); }
        // A missing or broken file means defaults: preferences must never stop enforcement.
        public static Settings Read(string admin) {
            try {
                string path = PathOf(admin);
                if (File.Exists(path)) return ChildFiles.FromJson<Settings>(File.ReadAllText(path)) ?? new Settings();
            } catch { }
            return new Settings();
        }
        public static void Update(string admin, Action<Settings> change) {
            ConfigStore.WithLock(() => {
                var s = Read(admin);
                change(s);
                ConfigStore.WriteAtomic(PathOf(admin), ChildFiles.ToJson(s));
            });
        }
    }
}
