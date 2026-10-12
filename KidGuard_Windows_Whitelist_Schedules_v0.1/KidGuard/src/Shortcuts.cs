using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace KidGuard {
    // The Parent Panel's desktop and Start menu shortcuts for the installing administrator, with the shield
    // taken from KidGuardPanel.exe itself. Made by KidGuard.exe --panel-shortcuts (setup) and checked again
    // by the service and the panel, so a working installation never depends on a separate script.
    public static class PanelShortcuts {
        public const string Name = "KidGuard Parent Panel.lnk";
        static readonly string[] Readable = { "KidGuardPanel.exe", "kidguard.ico" };
        static readonly SecurityIdentifier Users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        // S-1-15-2-1: the Windows 11 Start menu runs as an app package and needs its own read permission to show the icon.
        static readonly SecurityIdentifier AppPackages = new SecurityIdentifier("S-1-15-2-1");

        // The program folder stays Administrators/SYSTEM only. Explorer and the Start menu may read and run these
        // two files only, so they can show the icon and start the panel (which still asks for administrator approval).
        public static void GrantRead(string root) {
            foreach (string name in Readable) {
                string file = Path.Combine(root, name);
                if (!File.Exists(file) || HasRead(file)) continue;
                var security = File.GetAccessControl(file);
                foreach (var who in new[] { Users, AppPackages })
                    security.AddAccessRule(new FileSystemAccessRule(who, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                File.SetAccessControl(file, security);
            }
        }
        static bool HasRead(string file) {
            bool users = false, packages = false;
            foreach (FileSystemAccessRule rule in File.GetAccessControl(file).GetAccessRules(true, true, typeof(SecurityIdentifier))) {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & FileSystemRights.ReadAndExecute) != FileSystemRights.ReadAndExecute) continue;
                if (rule.IdentityReference.Equals(Users)) users = true;
                if (rule.IdentityReference.Equals(AppPackages)) packages = true;
            }
            return users && packages;
        }

        public static List<string> Paths() {
            return new List<string> {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name)
            };
        }

        // Creates both shortcuts (or replaces them) for the current user. Returns their paths.
        public static List<string> Create(string root) {
            GrantRead(root);
            var paths = Paths();
            foreach (string path in paths) Write(path, root);
            return paths;
        }

        // Rewrites existing shortcuts that point elsewhere or show no icon; never adds one the parent deleted.
        public static void Repair(string root) {
            GrantRead(root);
            string exe = Path.Combine(root, "KidGuardPanel.exe");
            foreach (string path in Paths()) {
                if (!File.Exists(path)) continue;
                object link = Open(path);
                string target = (string)Get(link, "TargetPath"), icon = (string)Get(link, "IconLocation");
                if (!string.Equals(target, exe, StringComparison.OrdinalIgnoreCase) || !string.Equals(icon, exe + ",0", StringComparison.OrdinalIgnoreCase))
                    Write(path, root);
            }
        }

        static void Write(string path, string root) {
            string exe = Path.Combine(root, "KidGuardPanel.exe");
            object link = Open(path);
            Set(link, "TargetPath", exe);
            Set(link, "WorkingDirectory", root);
            Set(link, "Description", "KidGuard parental controls");
            Set(link, "IconLocation", exe + ",0");
            link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        static object Open(string path) {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        }
        static object Get(object o, string name) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }
        static void Set(object o, string name, object value) { o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { value }); }
    }
}
