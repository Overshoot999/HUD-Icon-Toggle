using HarmonyLib;
using System.Linq;
using Rewired;

namespace HUDIconToggle
{
    [HarmonyPatch(typeof(InputManager_Base), "Awake")]
    public static class RewiredActionInjector
    {
        static void Prefix(InputManager_Base __instance)
        {
            HUDIconTogglePlugin.Log.LogInfo("RewiredActionInjector: InputManager_Base.Awake Prefix triggered!");
            try
            {
                InjectActions(__instance);
            }
            catch (System.Exception ex)
            {
                HUDIconTogglePlugin.Log.LogError($"RewiredActionInjector: Exception in Prefix/InjectActions: {ex}");
            }
        }

        private static void InjectActions(InputManager_Base manager)
        {
            HUDIconTogglePlugin.Log.LogInfo("RewiredActionInjector: Starting InjectActions...");
            
            var userDataField = typeof(InputManager_Base).GetField("_userData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (userDataField == null)
            {
                HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: _userData field not found on InputManager_Base!");
                return;
            }
            
            HUDIconTogglePlugin.Log.LogInfo("RewiredActionInjector: Found _userData field, retrieving value...");
            var userData = userDataField.GetValue(manager);
            if (userData == null)
            {
                HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: _userData value is null on InputManager_Base!");
                return;
            }
            
            HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Found _userData ({userData.GetType().FullName}). Retrieving actions list...");

            // Get actions list
            var actionsField = userData.GetType().GetField("actions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var actions = actionsField?.GetValue(userData) as System.Collections.Generic.List<InputAction>;
            if (actions == null)
            {
                HUDIconTogglePlugin.Log.LogInfo("RewiredActionInjector: actions field cast failed, trying property...");
                var actionsProp = userData.GetType().GetProperty("actions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                actions = actionsProp?.GetValue(userData) as System.Collections.Generic.List<InputAction>;
            }
            
            if (actions == null)
            {
                HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: actions list is null!");
                return;
            }
            HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Found actions list with {actions.Count} existing actions. Retrieving categories...");

            // Get actionCategories list
            var categoriesField = userData.GetType().GetField("actionCategories", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var categories = categoriesField?.GetValue(userData) as System.Collections.Generic.List<InputCategory>;
            if (categories == null)
            {
                HUDIconTogglePlugin.Log.LogInfo("RewiredActionInjector: actionCategories field cast failed, trying property...");
                var categoriesProp = userData.GetType().GetProperty("actionCategories", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                categories = categoriesProp?.GetValue(userData) as System.Collections.Generic.List<InputCategory>;
            }
            
            if (categories == null)
            {
                HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: actionCategories list is null!");
                return;
            }
HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Found categories list with {categories.Count} categories.");
            
            // Log all available category names for reference
            foreach (var cat in categories)
            {
                HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Available category: '{cat.name}' (id={cat.id})");
            }

// Fallback category used if modAction.Category is null or not found.
            var fallbackCategory = categories.FirstOrDefault(c => c.name == "Debug") ?? categories.FirstOrDefault();
            if (fallbackCategory == null)
            {
                HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: No categories found at all!");
                return;
            }

            HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Fallback category is '{fallbackCategory.name}' (id={fallbackCategory.id}).");

            int nextId = GetNextActionId(actions);
            HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Determined nextActionId = {nextId}. Injecting pending actions...");

            foreach (var modAction in ExtraInputManager.PendingActions)
            {
                HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Processing pending action '{modAction.Name}'...");
                if (actions.Any(a => a.name == modAction.Name))
                {
                    HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Action '{modAction.Name}' already exists, skipping injection.");
                    continue;
                }

                // Resolve target category from modAction.Category, fallback if missing.
                var targetCategory = fallbackCategory;
                if (!string.IsNullOrEmpty(modAction.Category))
                {
                    var resolved = categories.FirstOrDefault(c => c.name == modAction.Category);
                    if (resolved != null)
                    {
                        targetCategory = resolved;
                        HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Resolved category '{modAction.Category}' for action '{modAction.Name}'.");
                    }
                    else
                    {
                        HUDIconTogglePlugin.Log.LogWarning($"RewiredActionInjector: Category '{modAction.Category}' not found; using fallback '{fallbackCategory.name}'.");
                    }
                }

                var action = new InputAction();
                SetField(action, "id", nextId++);
                SetField(action, "name", modAction.Name);
                SetField(action, "type", modAction.Type);
                SetField(action, "descriptiveName", modAction.Name);
                SetField(action, "categoryId", targetCategory.id);
                SetField(action, "_userAssignable", true);

                actions.Add(action);
                HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Injected '{modAction.Name}' action object into list.");

                // Invoke userData.actionCategoryMap.AddAction(categoryId, actionId)
                var categoryMapField = userData.GetType().GetField("actionCategoryMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var categoryMap = categoryMapField?.GetValue(userData);
                if (categoryMap != null)
                {
                    var addActionMethod = categoryMap.GetType().GetMethod("AddAction", new System.Type[] { typeof(int), typeof(int) });
                    if (addActionMethod != null)
                    {
                        addActionMethod.Invoke(categoryMap, new object[] { targetCategory.id, action.id });
                        HUDIconTogglePlugin.Log.LogInfo($"RewiredActionInjector: Mapped '{modAction.Name}' (ID={action.id}) to category (ID={targetCategory.id}) in categoryMap.");
                    }
                    else
                    {
                        HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: AddAction method not found on actionCategoryMap!");
                    }
                }
                else
                {
                    HUDIconTogglePlugin.Log.LogWarning("RewiredActionInjector: actionCategoryMap field is null!");
                }

                modAction.AssignedId = action.id;
            }
            ExtraInputManager.RewiredInitialized = true;
            HUDIconTogglePlugin.Log.LogInfo("RewiredActionInjector: Action injection successfully completed!");
        }

        private static void SetField(object obj, string fieldName, object value)
        {
            if (obj == null) return;
            var t = obj.GetType();
            
            // Try setting direct field
            var field = t.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(obj, value);
                return;
            }

            // Try backing field name
            string backingName = $"<{fieldName}>k__BackingField";
            field = t.GetField(backingName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(obj, value);
                return;
            }

            // Try with prefix underscore
            field = t.GetField("_" + fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(obj, value);
                return;
            }

            // Try setting property if writable
            var prop = t.GetProperty(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value, null);
            }
        }

        private static int GetNextActionId(System.Collections.Generic.List<InputAction> actions)
        {
            if (actions.Count == 0)
                return 1000;

            return actions.Max(a => a.id) + 1;
        }
    }
}
