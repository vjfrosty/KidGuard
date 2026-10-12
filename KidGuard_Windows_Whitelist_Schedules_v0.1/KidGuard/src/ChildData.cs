using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KidGuard {
    // Shared by the service, the Parent Panel and the child's helper (KidGuardTray.exe, which compiles this file
    // itself: the child cannot read the KidGuard program folder). No other KidGuard types are used here.

    // Something the child asked for: a blocked site, or more time. Stored in Admin\requests.json.
    [DataContract] public class ChildRequest {
        [DataMember] public string id;
        // "site" or "time".
        [DataMember] public string kind;
        [DataMember(EmitDefaultValue=false)] public string site;
        [DataMember(EmitDefaultValue=false)] public int minutes;
        [DataMember(EmitDefaultValue=false)] public string note;
        [DataMember] public string asked;
        // "waiting", "allowed", "denied" or "expired".
        [DataMember] public string state;
        [DataMember(EmitDefaultValue=false)] public string answered;
        // What the parent granted, e.g. "wikipedia.org, *.wikipedia.org" or "+30".
        [DataMember(EmitDefaultValue=false)] public string answer;
    }

    // Written by the service for the child's helper (ChildFiles.StatusPath, readable by the child).
    [DataContract] public class ChildStatus {
        [DataMember] public string updated;
        [DataMember] public string language;
        // Time zone of the schedule, so the helper shows the same clock times as the Parent Panel.
        [DataMember] public string zone;
        [DataMember] public bool accountAllowed;
        [DataMember(EmitDefaultValue=false)] public string signOutAt;
        [DataMember] public bool internetAllowed;
        [DataMember(EmitDefaultValue=false)] public string internetChangeAt;
        // Sites blocked in the last 30 minutes, newest first: what the child most likely wants to ask for.
        [DataMember] public string[] recentBlocked;
        [DataMember] public ChildRequest[] requests;
    }

    public static class ChildFiles {
        public const int MaxNote = 200, MaxRequestBytes = 2048;
        public static readonly int[] TimeChoices = { 15, 30, 60 };
        // Administrators and SYSTEM write; the child reads. Only Inbox accepts new files from the child.
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KidGuard-Child"); } }
        public static string StatusPath { get { return Path.Combine(Dir, "status.json"); } }
        public static string InboxDir { get { return Path.Combine(Dir, "inbox"); } }
        public static string HelperPath { get { return Path.Combine(Dir, "KidGuardTray.exe"); } }

        public static T FromJson<T>(string json) {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }
        public static string ToJson<T>(T value) {
            using (var ms = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
        // A request file from the child: "kind=site", "site=...", "minutes=30", "note=..." lines.
        public static string RequestText(string kind, string site, int minutes, string note) {
            var b = new StringBuilder("kind=" + kind + "\n");
            if (site != null) b.Append("site=" + OneLine(site) + "\n");
            if (minutes > 0) b.Append("minutes=" + minutes + "\n");
            if (!String.IsNullOrEmpty(note)) b.Append("note=" + OneLine(note) + "\n");
            return b.ToString();
        }
        static string OneLine(string s) { return s.Replace("\r", " ").Replace("\n", " ").Trim(); }
        // Unvalidated fields; the service validates them (the child wrote this file).
        public static ChildRequest ParseRequest(string text) {
            var r = new ChildRequest();
            foreach (string raw in text.Split('\n')) {
                string line = raw.TrimEnd('\r');
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1).Trim();
                if (key == "kind") r.kind = value;
                else if (key == "site") r.site = value;
                else if (key == "note") r.note = value;
                else if (key == "minutes") { int m; if (Int32.TryParse(value, out m)) r.minutes = m; }
            }
            return r;
        }
    }
}
