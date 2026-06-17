using System;
using System.Collections.Generic;
using System.IO;
using Rewired;
using BepInEx;

namespace HUDIconToggle
{
    public class ModInputAction
    {
        public string Name { get; set; }
        public Rewired.InputActionType Type { get; set; }
        public string Category { get; set; }
        public int AssignedId { get; set; }

        public ModInputAction(string name, Rewired.InputActionType type, string category = null)
        {
            Name = name;
            Type = type;
            Category = category;
            AssignedId = -1;
        }
    }

    public static class ExtraInputManager
    {
        public static List<ModInputAction> PendingActions = new List<ModInputAction>();
        public static bool RewiredInitialized = false;

        private static string ConfigPath => Path.Combine(Path.GetDirectoryName(typeof(ExtraInputManager).Assembly.Location) ?? "", "ExtraInputActions.json");

public static void RegisterAction(string actionName, InputActionType type, string category = null)
        {
            if (PendingActions.Exists(a => a.Name == actionName))
                return;

            PendingActions.Add(new ModInputAction(actionName, type, category));
            // Save is batched - call SavePendingActions() explicitly when needed
        }

        public static void LoadPendingActions()
        {
            // Statically registered via Plugin.Awake, so loading from disk is not necessary.
            // This avoids any complex parsing or JSON dependencies.
        }

        public static void SavePendingActions()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < PendingActions.Count; i++)
                {
                    var act = PendingActions[i];
                    sb.AppendLine("  {");
                    sb.AppendLine($"    \"Name\": \"{act.Name}\",");
                    sb.AppendLine($"    \"Type\": {(int)act.Type},");
                    sb.AppendLine($"    \"Category\": \"{act.Category ?? "null"}\",");
                    sb.AppendLine($"    \"AssignedId\": {act.AssignedId}");
                    sb.Append("  }");
                    if (i < PendingActions.Count - 1)
                        sb.AppendLine(",");
                    else
                        sb.AppendLine();
                }
                sb.AppendLine("]");
                
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ExtraInputManager] Error saving actions: {ex}");
            }
        }
    }
}
