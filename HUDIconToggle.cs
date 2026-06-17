using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Rewired;

namespace HUDIconToggle
{
    [BepInPlugin(GUID, "HUD Icon Toggle", VERSION)]
public class HUDIconTogglePlugin : BaseUnityPlugin
    {
        public const string GUID    = "com.hudmodding.nuclearoption.hudicontoggle";
        public const string VERSION = "1.0.4";

private const int CONFIG_VERSION = 5;

        internal static ManualLogSource Log;

        // ── Config ────────────────────────────────────────────────────────────

//        private ConfigEntry<KeyboardShortcut> _keyDump;
//        private ConfigEntry<KeyboardShortcut> _keyDumpAllUnits;



        // ── Visibility toggles (config-menu mirrors of the keybind state) ──────
        // These are the actual source of truth: keybinds flip these entries,
        // and the config menu can flip them directly too. A SettingChanged
        // handler re-applies visibility whenever either happens.
        private ConfigEntry<bool>[]   _factionVisCfg; // [faction]
        private ConfigEntry<bool>[,]  _catVisCfg;      // [faction, category]

// Guards against re-entrant Apply calls while we're updating config
        // entries in bulk (e.g. HandleMasterToggle writing 21 entries at once).
        private bool _suppressConfigCallback;

        // ── Notification config ─────────────────────────────────────────────────
        private ConfigEntry<bool>   _notifEnabled;
        private ConfigEntry<float>    _notifX;
        private ConfigEntry<float>    _notifY;
        private ConfigEntry<float>   _notifDuration;

        // Notification state
        private GameObject _notifPanel;
        private Text      _notifText;
        private float     _notifTimer;

        // ── Visibility state ──────────────────────────────────────────────────
        //
        // There is NO separate master-visible flag. All state lives in two
        // config-backed grids:
        //
        //   _factionVisCfg[fi]    — faction master switch (Friendly/Enemy/Neutral)
        //   _catVisCfg[fi, ci]    — per-faction per-category cell
        //
        // ResolveVisible = _factionVisCfg[fi].Value && _catVisCfg[fi, ci].Value
        //
        // The "master toggle" key is just a convenience that writes false/true
        // into every cell and every faction switch at once. Because there is no
        // hidden flag, any subsequent targeted key press operates directly on
        // the grid and is immediately effective. Config entries are bound to
        // SettingChanged so toggling a checkbox in the config menu re-applies
        // visibility immediately too.

        // Built once at startup from ScriptableObject type names — maps unit
        // display name (lowercase) → category, derived from the game's own
        // definition class (AircraftDefinition, ShipDefinition, etc.).
        // This is the primary classification source; keyword Rules are fallback.
        private readonly Dictionary<string, IconCategory> _typeMap
            = new Dictionary<string, IconCategory>(256, StringComparer.OrdinalIgnoreCase);

        // ── Icon cache ────────────────────────────────────────────────────────

        private readonly List<IconEntry>     _icons = new List<IconEntry>(256);
        private readonly HashSet<GameObject> _known = new HashSet<GameObject>();
        private readonly List<IconEntry>[,]  _grid  = new List<IconEntry>[3, 6];

        private Transform _iconLayer;
        private int       _layerChildCount = -1;
        private float     _cleanupTimer;
        private const float CLEANUP_INTERVAL = 5f;

        private const string HUDCANVAS_PATH = "SceneEssentials/Canvas/HUDCanvas";
        private const string ICONLAYER_NAME = "IconLayer";
        private const float  FACTION_DELTA  = 0.15f;

        // ── Classification rules (first match wins) ───────────────────────────

private static readonly (IconCategory cat, string[] kw)[] Rules =
        {
            ( IconCategory.Aircraft, new[] {
                "a-19", "a-19c", "alkyon", "anvil", "attackhelo",
                "bomber",
                "cas1", "chicane", "ci-22", "coin", "compass", "cricket",
                "darkreach", "drone",
                "ea-25b", "ew-25", "ew1",
                "f-99", "f-16m", "fastbomber", "fighter", "fighter1", "fs-12", "fs-12v", "fs-20", "fs-20b", "fs-3ex", "fq-106",
                "heli", "helicopter",
                "jet", "kestrel", "kr-67", "kr-67a",
                "longsword",
                "mc-260", "medusa", "mig-15", "multirole",
                "quadvtol",
                "rah-72",
                "saber", "sah-46", "sfb", "sfb-81", "shrike", "smallfighter",
                "t/a-30", "t/a-30yh", "ternion", "trainer",
                "uav", "uf-0", "uh-90", "uh-90k", "ufo", "utilityhelo",
                "vl-49", "vl-49d", "vtol" } ),

            ( IconCategory.Buildings, new[] {
                "aa gun container", "agm-98 launcher container",
                "airfield", "aircraft revetment",
                "ammo dump", "ammo storage",
                "barracks", "bunker",
                "command post", "control tower",
                "emplacement", "enrichment plant",
                "factory", "fortif", "fuel depot", "fuel storage",
                "generator", "guard tower",
                "hardened aircraft shelter", "hangar", "helipad",
                "large factory",
                "munitions pallet",
                "outpost",
                "pillbox",
                "radar station", "radar tower", "refinery",
                "storage tank", "structure",
                "vehicle depot", "vertical factory" } ),

            ( IconCategory.Ground, new[] {
                "aa gun container", "aerosentry", "afv", "afv-", "afv6", "agm-98 launcher container", "apc", "apc-", "apm-71", "artillery",
                "boltstrike", "bs200",
                "field deployable airpad", "fire control", "flatbed", "frcv-105", "fuel tanker",
                "fga-30", "fga-57",
                "hexhound", "horse", "hlt", "hlt-", "howitzer",
                "ifv", "jackknife",
                "launcher container", "lcv25", "lcv45", "lcv-", "lcv-25", "lcv-45", "linebreaker",
                "mbt", "mobile air defense", "mlrs", "mortar", "msv", "msv-", "munitions truck",
                "radar container", "radar truck", "ram-45 launcher container", "ram45 launcher", "ram45 sam launcher", "r9 stratolance sam launcher", "recon truck",
                "sam", "slmmr-", "spaa", "spaag", "spearhead", "stratolance",
                "t9k41", "tractor", "type-12", "type-14",
                "wreck mbt" } ),

            ( IconCategory.Missiles, new[] {
                "aam-", "agm-", "agr-", "air-2", "alm-", "alnd-", "arad-", "arm-", "ashm-", "asm", "at-145", "atgm", "atp-",
                "bomb",
                "cbo-", "cruise",
                "demolition bomb", "dt-1600",
                "eyeball",
                "gbm-", "glide", "glr-04", "gpo-", "guided shell", "gs25",
                "hasm-",
                "irm-",
                "mmr-", "missile",
                "nl-98",
                "pab-", "piledriver",
                "r6 longsword", "r9 stratolance", "ram-45", "rocket",
                "sam ir", "shell", "tusko",
                "tbm-", "tbm", "torpedo",
                "vlm-", "warhead" } ),

            ( IconCategory.Naval, new[] {
                "annex", "argus", "assault carrier",
                "battleship",
                "carrier", "corvette", "cruiser", "cursor",
                "destroyer", "dynamo",
                "frigate",
                "hyperion",
                "landing craft",
                "otb-",
                "patrol boat",
                "shard class", "ship", "submarine",
                "vessel" } ),
        };

        // OPTIMIZATION: Pre-built keyword lookup for O(1) classification instead of O(n*m)
        private static readonly Dictionary<string, IconCategory> s_keywordLookup;
        static HUDIconTogglePlugin()
        {
            s_keywordLookup = new Dictionary<string, IconCategory>(128, StringComparer.OrdinalIgnoreCase);
            foreach (var (cat, kws) in Rules)
            {
                foreach (var kw in kws)
                    s_keywordLookup[kw] = cat;
            }
        }

private static readonly HashSet<string> Ignored =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "targetArrow", "targetText", "TargetCode",
              "objectivePointer", "ObjectiveInfo" };

        // Static — compiled once, reused on every Classify call.
        private static readonly System.Text.RegularExpressions.Regex s_netIdSuffix
            = new System.Text.RegularExpressions.Regex(
                @"\[\d+\]$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // ── Helper arrays for iteration ───────────────────────────────────────

        private static readonly IconFaction[]  AllFactions   = (IconFaction[]) Enum.GetValues(typeof(IconFaction));
        private static readonly IconCategory[] AllCategories = (IconCategory[])Enum.GetValues(typeof(IconCategory));

        // Maps the three keyed factions (index 0/1/2) to IconFaction enum values
        private static readonly IconFaction[] KeyFactions =
            { IconFaction.Friendly, IconFaction.Enemy, IconFaction.Neutral };

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Log = Logger;

            for (int f = 0; f < 3; f++)
                for (int c = 0; c < 6; c++)
                    _grid[f, c] = new List<IconEntry>(32);

            BindConfig();
            RegisterRewiredActions();
            BuildTypeMap();
            new Harmony(GUID).PatchAll();
            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo($"HUD Icon Toggle v{VERSION} loaded. (config v{CONFIG_VERSION})");
            // Keybind logging removed: all toggle input is now Rewired/ExtraInputManager.
            // (Visibility toggles remain in the config menu.)
        }

private void RegisterRewiredActions()
        {
            const string rewiredCategory = "Gameplay";

            ExtraInputManager.LoadPendingActions();
            ExtraInputManager.RegisterAction("Toggle All Icons", Rewired.InputActionType.Button, rewiredCategory);
            ExtraInputManager.RegisterAction("Toggle All Friendlies Icons", Rewired.InputActionType.Button, rewiredCategory);
            ExtraInputManager.RegisterAction("Toggle All Enemies Icons", Rewired.InputActionType.Button, rewiredCategory);
            ExtraInputManager.RegisterAction("Toggle All Neutrals Icons", Rewired.InputActionType.Button, rewiredCategory);
            
            string[] factions = { "Friendly", "Enemy", "Neutral" };
            for (int f = 0; f < 3; f++)
            {
                for (int c = 0; c < 6; c++)
                {
                    IconCategory cat = (IconCategory)c;
                    ExtraInputManager.RegisterAction($"Toggle {factions[f]} {cat} Icons", Rewired.InputActionType.Button, rewiredCategory);
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Rebuild the type map on each scene load so late-loading mod
            // definitions (loaded after our Awake) are always picked up.
            // Also reset the icon layer ref — it's recreated per mission.
            _iconLayer       = null;
            _layerChildCount = -1;
            BuildTypeMap();
        }

        // ── Config binding ────────────────────────────────────────────────────

        private void BindConfig()
        {
            _factionVisCfg = new ConfigEntry<bool>[3];
            _catVisCfg     = new ConfigEntry<bool>[3, 6];

            // ── Master Toggles (checkbox state only) ─────────────────────────
            _factionVisCfg[(int)IconFaction.Friendly] = Config.Bind(
                "Master Toggles",
                "Friendlies Visible",
                true,
                new ConfigDescription(
                    "Current visibility state for all friendly icons.",
                    null, new ConfigurationManagerAttributes { Order = 89 }));

            _factionVisCfg[(int)IconFaction.Enemy] = Config.Bind(
                "Master Toggles",
                "Enemies Visible",
                true,
                new ConfigDescription(
                    "Current visibility state for all enemy icons.",
                    null, new ConfigurationManagerAttributes { Order = 79 }));

            _factionVisCfg[(int)IconFaction.Neutral] = Config.Bind(
                "Master Toggles",
                "Neutrals Visible",
                true,
                new ConfigDescription(
                    "Current visibility state for all neutral icons.",
                    null, new ConfigurationManagerAttributes { Order = 69 }));

            // ── Per-faction category visibility (checkbox state only) ─────
            // Categories map to IconCategory enum order:
            //   Missiles, Buildings, Aircraft, Naval, Ground, Other

            string[] sections = { "Friendly Units", "Enemy Units", "Neutral Units" };

            for (int fi = 0; fi < 3; fi++)
            {
                string factionLabel = sections[fi].Replace(" Units", "");
                for (int ci = 0; ci < 6; ci++)
                {
                    IconCategory cat = (IconCategory)ci;
                    int order = (6 - ci) * 10; // higher order = shown first

                    _catVisCfg[fi, ci] = Config.Bind(
                        sections[fi],
                        $"{cat} Visible",
                        true,
                        new ConfigDescription(
                            $"Current visibility state for {factionLabel} {cat} icons.",
                            null, new ConfigurationManagerAttributes { Order = order - 1 }));
                }
            }

// React to in-menu checkbox changes (and to our own writes below).
            foreach (var e in _factionVisCfg) e.SettingChanged += OnVisibilityConfigChanged;
            foreach (var e in _catVisCfg)     e.SettingChanged += OnVisibilityConfigChanged;

            // ── Notification Config ─────────────────────────────────────────────────
            _notifEnabled = Config.Bind(
                "Notifications",
                "Enabled",
                true,
                new ConfigDescription(
                    "Show popup notifications when toggling icons.",
                    null, new ConfigurationManagerAttributes { Order = 99 }));

_notifX = Config.Bind(
                "Notifications",
                "PositionX",
                20f,
                new ConfigDescription(
                    "X position of notification text (from left edge).",
                    null, new ConfigurationManagerAttributes { Order = 98 }));

            _notifY = Config.Bind(
                "Notifications",
                "PositionY",
                100f,
                new ConfigDescription(
                    "Y position of notification text (from top edge).",
                    null, new ConfigurationManagerAttributes { Order = 97 }));

            _notifDuration = Config.Bind(
                "Notifications",
                "DisplayDuration",
                2f,
                new ConfigDescription(
                    "How long to show notifications (seconds).",
                    null, new ConfigurationManagerAttributes { Order = 96 }));
        }

        // Fired whenever a visibility checkbox changes, whether from a keybind
        // (we write the ConfigEntry ourselves) or from the user editing the
        // config menu directly. Re-applies visibility to match the new state.
        private void OnVisibilityConfigChanged(object sender, EventArgs e)
        {
            if (_suppressConfigCallback) return;
            ApplyAll();
        }

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            ScanForNewIcons();

_cleanupTimer -= Time.unscaledDeltaTime;
            if (_cleanupTimer <= 0f)
            {
                PruneDeadEntries();
                _cleanupTimer = CLEANUP_INTERVAL;
            }

            // Update notification fade
            if (_notifTimer > 0f)
            {
                _notifTimer -= Time.unscaledDeltaTime;
                if (_notifPanel != null)
                {
                    float alpha = Mathf.Clamp01(_notifTimer / _notifDuration.Value);
                    var cg = _notifPanel.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = alpha;
                }
                if (_notifTimer <= 0f && _notifPanel != null)
                {
                    Destroy(_notifPanel);
                    _notifPanel = null;
                }
            }

            // Prevent toggle input when in chat
            bool inChat = false;
            try { inChat = CursorManager.GetFlag(CursorFlags.Chat); } catch {}
            if (inChat) return;

            Rewired.Player localPlayer = null;
            if (ExtraInputManager.RewiredInitialized)
            {
                localPlayer = Rewired.ReInput.players.GetPlayer(0);
            }

            bool CheckToggleDown(string actionName)
            {
                return localPlayer != null && localPlayer.GetButtonDown(actionName);
            }

// Rewired action checks — early return on first match to avoid double-firing.
            if (CheckToggleDown("Toggle All Icons"))                 { HandleMasterToggle(); return; }
            if (CheckToggleDown("Toggle All Friendlies Icons"))          { HandleFactionToggle(IconFaction.Friendly); return; }
            if (CheckToggleDown("Toggle All Enemies Icons"))             { HandleFactionToggle(IconFaction.Enemy); return; }
            if (CheckToggleDown("Toggle All Neutrals Icons"))            { HandleFactionToggle(IconFaction.Neutral); return; }

            string[] factions = { "Friendly", "Enemy", "Neutral" };
            for (int fi = 0; fi < 3; fi++)
            {
                for (int ci = 0; ci < 6; ci++)
                {
                    string actionName = $"Toggle {factions[fi]} {(IconCategory)ci} Icons";
                    if (!CheckToggleDown(actionName)) continue;
                    HandleCategoryToggle(KeyFactions[fi], (IconCategory)ci);
                    return;
                }
            }
        }

        // ── Toggle handlers ───────────────────────────────────────────────────

        // Master toggle: hide-all / restore-all.
        // Intent is derived from actual state: if anything is currently hidden
        // (any faction switch off, or any grid cell false), restore everything.
        // Only hides when everything is already fully visible.
        private void HandleMasterToggle()
        {
            bool anythingHidden = false;
            for (int f = 0; f < 3; f++)
            {
                if (!_factionVisCfg[f].Value) { anythingHidden = true; break; }
                for (int c = 0; c < 6; c++)
                    if (!_catVisCfg[f, c].Value) { anythingHidden = true; break; }
                if (anythingHidden) break;
            }

            _suppressConfigCallback = true;
            try
            {
                if (anythingHidden)
                {
                    // Restore everything.
                    for (int f = 0; f < 3; f++)
                    {
                        _factionVisCfg[f].Value = true;
                        for (int c = 0; c < 6; c++)
                            _catVisCfg[f, c].Value = true;
                    }
                    Log.LogDebug("All icons SHOWN.");
                }
                else
                {
                    // Everything visible — hide it all.
                    for (int f = 0; f < 3; f++)
                    {
                        _factionVisCfg[f].Value = false;
                        for (int c = 0; c < 6; c++)
                            _catVisCfg[f, c].Value = true; // cells stay true; faction switch does the hiding
                    }
                    Log.LogDebug("All icons HIDDEN. (any targeted key will override this)");
                }
            }
            finally
            {
                _suppressConfigCallback = false;
            }

ApplyAll();
            ShowNotification(anythingHidden ? "All Icons: SHOWN" : "All Icons: HIDDEN");
        }

        // Faction master: flip show/hide for all icons of that faction;
        // resets per-category cells so they can't silently contradict the switch.
        private void HandleFactionToggle(IconFaction fac)
        {
            int fi = (int)fac;
            bool newVal = !_factionVisCfg[fi].Value;

            _suppressConfigCallback = true;
            try
            {
                _factionVisCfg[fi].Value = newVal;

                // Reset category cells — the faction switch is now authoritative.
                for (int c = 0; c < 6; c++)
                    _catVisCfg[fi, c].Value = true;
            }
            finally
            {
                _suppressConfigCallback = false;
            }

ApplyFactionRows(fac);
            Log.LogDebug($"{fac} (all): {(newVal ? "SHOWN" : "HIDDEN")} " +
                        $"({CountFaction(fac)} icons) — category overrides reset.");
            ShowNotification($"{fac}: {(newVal ? "SHOWN" : "HIDDEN")}");
        }

        // Category key: flip one faction/category cell.
        // If the faction master is currently OFF, this is treated as an explicit
        // "show this type" intent — restore the faction master, reset all cells
        // for that faction to true, then ensure the target cell is visible.
        // We do NOT toggle in this case: the player's first press should always show.
        private void HandleCategoryToggle(IconFaction fac, IconCategory cat)
        {
            int fi = (int)fac;
            int ci = (int)cat;

            if (!_factionVisCfg[fi].Value)
            {
                // Faction was globally hidden — restore it and show only this category.
                _suppressConfigCallback = true;
                try
                {
                    _factionVisCfg[fi].Value = true;
                    for (int c = 0; c < 6; c++)
                        _catVisCfg[fi, c].Value = false;  // hide everything for this faction...
                    _catVisCfg[fi, ci].Value = true;       // ...then show only the requested cell
                }
                finally
                {
                    _suppressConfigCallback = false;
                }

ApplyFactionRows(fac);
                Log.LogDebug($"[{fac}] {cat}: SHOWN (from hidden) ({CountFactionCategory(fac, cat)} icons)");
                ShowNotification($"{fac} {cat}: SHOWN");
                return;
            }

            // Normal case: faction is visible, just toggle the cell.
            bool newVal = !_catVisCfg[fi, ci].Value;
            _catVisCfg[fi, ci].Value = newVal; // fires OnVisibilityConfigChanged → ApplyAll

            Log.LogDebug($"[{fac}] {cat}: {(newVal ? "SHOWN" : "HIDDEN")} " +
                        $"({CountFactionCategory(fac, cat)} icons)");
            ShowNotification($"{fac} {cat}: {(newVal ? "SHOWN" : "HIDDEN")}");
        }

        // ── Cache / scan ──────────────────────────────────────────────────────

        // Static — allocated once, never changes.
        private static readonly Dictionary<string, IconCategory> s_typeToCategory
            = new Dictionary<string, IconCategory>(StringComparer.Ordinal)
        {
            { "AircraftDefinition",  IconCategory.Aircraft  },
            { "AircraftParameters",  IconCategory.Aircraft  },
            { "BuildingDefinition",  IconCategory.Buildings },
            { "MissileDefinition",   IconCategory.Missiles  },
            { "ShipDefinition",      IconCategory.Naval     },
            { "VehicleDefinition",   IconCategory.Ground    },
            { "UnitDefinition",      IconCategory.Ground    },
            // SceneryDefinition intentionally absent — props never show as icons.
        };

        private void BuildTypeMap()
        {
            _typeMap.Clear();

            int mapped = 0;
            foreach (var obj in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (!s_typeToCategory.TryGetValue(obj.GetType().Name, out var cat)) continue;

                string displayName = GetDisplayName(obj) ?? obj.name;
                if (string.IsNullOrEmpty(displayName)) continue;

                // Store both display name and asset name (lowercased) so either
                // form that might appear as a spawned icon name will match.
                _typeMap[displayName.ToLowerInvariant()] = cat;
                if (!string.Equals(displayName, obj.name, StringComparison.OrdinalIgnoreCase))
                    _typeMap[obj.name.ToLowerInvariant()] = cat;

                mapped++;
            }

            Log.LogInfo($"TypeMap built: {mapped} definitions → {_typeMap.Count} name entries.");
        }

        private Transform GetIconLayer()
        {
            if (_iconLayer != null && _iconLayer.gameObject != null) return _iconLayer;
            _iconLayer = null;
            var hud = GameObject.Find(HUDCANVAS_PATH);
            if (hud == null) return null;
            _iconLayer = hud.transform.Find(ICONLAYER_NAME);
            _layerChildCount = _iconLayer != null ? _iconLayer.childCount : -1;
            return _iconLayer;
        }

        private void ScanForNewIcons()
        {
            var layer = GetIconLayer();
            if (layer == null) return;

            int current = layer.childCount;
            if (current == _layerChildCount) return;
            _layerChildCount = current;

            foreach (Transform child in layer)
            {
                var go = child.gameObject;
                if (_known.Contains(go)) continue;

                _known.Add(go);
                if (ShouldIgnore(go.name)) continue;

                var fac   = GetFactionFromColor(go);
                var cat   = Classify(go.name.ToLowerInvariant());
                var entry = new IconEntry(go, fac, cat);

                EnsureCanvasGroup(go);
                _icons.Add(entry);
                _grid[(int)fac, (int)cat].Add(entry);

                SetVisible(go, ResolveVisible(entry));
            }
        }

        private void PruneDeadEntries()
        {
            bool dirty = false;
            for (int i = _icons.Count - 1; i >= 0; i--)
            {
                if (_icons[i].Go != null) continue;
                _icons.RemoveAt(i);
                dirty = true;
            }

            if (!dirty) return;

            for (int f = 0; f < 3; f++)
                for (int c = 0; c < 6; c++)
                    _grid[f, c].Clear();

            foreach (var e in _icons)
                _grid[(int)e.Faction, (int)e.Category].Add(e);

            // Rebuild _known from surviving entries
            _known.Clear();
            foreach (var e in _icons) _known.Add(e.Go);
        }

        // ── Visibility application ────────────────────────────────────────────

        private void ApplyAll()
        {
            for (int i = _icons.Count - 1; i >= 0; i--)
            {
                var e = _icons[i];
                if (e.Go == null) { _icons.RemoveAt(i); continue; }
                SetVisible(e.Go, ResolveVisible(e));
            }
        }

        private void ApplyFactionRows(IconFaction fac)
        {
            int fi = (int)fac;
            for (int ci = 0; ci < 6; ci++)
            {
                var list = _grid[fi, ci];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Go == null) { list.RemoveAt(i); continue; }
                    SetVisible(list[i].Go, ResolveVisible(list[i]));
                }
            }
        }

        // Single source of truth: faction switch AND grid cell must both be true.
        private bool ResolveVisible(IconEntry e)
        {
            int fi = (int)e.Faction, ci = (int)e.Category;
            return _factionVisCfg[fi].Value && _catVisCfg[fi, ci].Value;
        }

        // ── SetVisible ────────────────────────────────────────────────────────

        private static void EnsureCanvasGroup(GameObject go)
        {
            if (go.GetComponent<CanvasGroup>() == null)
                go.AddComponent<CanvasGroup>();
        }

        private static void SetVisible(GameObject go, bool visible)
        {
            var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            cg.alpha          = visible ? 1f : 0f;
            cg.blocksRaycasts = visible;
            cg.interactable   = visible;
        }

        // ── Counters ──────────────────────────────────────────────────────────

        private int CountFaction(IconFaction fac)
        {
            int fi = (int)fac, n = 0;
            for (int ci = 0; ci < 6; ci++) n += _grid[fi, ci].Count;
            return n;
        }

private int CountFactionCategory(IconFaction fac, IconCategory cat)
            => _grid[(int)fac, (int)cat].Count;

// ── Notification system ────────────────────────────────────────────────────

        private void ShowNotification(string message)
        {
            if (!_notifEnabled.Value) return;

            Log.LogDebug($"Showing notification: {message}");

            // Destroy existing notification if any
            if (_notifPanel != null)
            {
                Destroy(_notifPanel);
                _notifPanel = null;
            }

            // Find the existing HUD canvas
            var hudCanvas = GameObject.Find(HUDCANVAS_PATH);
            if (hudCanvas == null)
            {
                Log.LogWarning("ShowNotification: HUDCanvas not found!");
                return;
            }

            // Create panel as child of existing HUD canvas
            _notifPanel = new GameObject("NotifPanel");
            _notifPanel.transform.SetParent(hudCanvas.transform, false);

            var rt = _notifPanel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(_notifX.Value, -_notifY.Value);
            rt.sizeDelta = new Vector2(300, 150);

            // Add CanvasGroup for fade
            var cg = _notifPanel.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            // Create text
            GameObject txtObj = new GameObject("NotifText");
            txtObj.transform.SetParent(_notifPanel.transform, false);

            var txtRt = txtObj.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            _notifText = txtObj.AddComponent<Text>();
            _notifText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _notifText.fontSize = 20;
            _notifText.alignment = TextAnchor.UpperLeft;
            _notifText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _notifText.verticalOverflow = VerticalWrapMode.Overflow;
            _notifText.color = Color.white;
            _notifText.supportRichText = true;

            // Add shadow for readability
            var shadow = txtObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(2, -2);

            _notifText.text = message;
            _notifTimer = _notifDuration.Value;
            Log.LogDebug($"Notification panel created at position ({_notifX.Value}, {-_notifY.Value})");
        }

        // ── Faction colour detection ──────────────────────────────────────────

        private static IconFaction GetFactionFromColor(GameObject go)
        {
            var img = go.GetComponent<Image>();
            if (img == null) return IconFaction.Neutral;

            float r = img.color.r, g = img.color.g, b = img.color.b;
            if (b - r > FACTION_DELTA && b > g * 0.5f) return IconFaction.Friendly; // blue ally
            if (g - r > FACTION_DELTA)                 return IconFaction.Friendly; // green player
            if (r - g > FACTION_DELTA)                 return IconFaction.Enemy;
            return IconFaction.Neutral;
        }

        // ── Classification ────────────────────────────────────────────────────

        private static bool ShouldIgnore(string name)
        {
            if (Ignored.Contains(name)) return true;
            if (name.StartsWith("hitmarker",    StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("radarWarning", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

private IconCategory Classify(string nl)
        {
            // Primary: type-map lookup. Strip the [NetID] suffix spawned icons append.
            string stripped = s_netIdSuffix.Replace(nl, "").Trim();
            if (_typeMap.TryGetValue(stripped, out var mapped)) return mapped;

            // OPTIMIZATION: O(1) keyword lookup instead of O(n*m) loop
            // Try substring match - iterate through known keywords and check if name contains them
            foreach (var kvp in s_keywordLookup)
            {
                if (nl.Contains(kvp.Key))
                    return kvp.Value;
            }

            return IconCategory.Other;
        }

        // ── Diagnostic dump ───────────────────────────────────────────────────

        private void DumpIconLayer()
        {
            Log.LogInfo("=== HUD ICON TOGGLE: DUMP START ===");
            var layer = GetIconLayer();
            if (layer == null)
            {
                Log.LogWarning("  IconLayer not found.");
                Log.LogInfo("=== HUD ICON TOGGLE: DUMP END ===");
                return;
            }

            Log.LogInfo($"  Path: {GetPath(layer)}  |  Children: {layer.childCount}  |  Cached: {_icons.Count}");
            Log.LogInfo($"  factionVis: Friendly={_factionVisCfg[0].Value}  Enemy={_factionVisCfg[1].Value}  Neutral={_factionVisCfg[2].Value}");
            Log.LogInfo("");

            Log.LogInfo("  visGrid (faction × category):");
            foreach (var fac in AllFactions)
            {
                var sb = new System.Text.StringBuilder($"    {fac,-10}: ");
                foreach (var cat in AllCategories)
                    sb.Append($"{cat}={_catVisCfg[(int)fac,(int)cat].Value}  ");
                Log.LogInfo(sb.ToString());
            }
            Log.LogInfo("");

            var counts = new int[3, 6];
            foreach (Transform child in layer)
            {
                string name = child.gameObject.name;
                if (ShouldIgnore(name)) continue;
                var cat = Classify(name.ToLowerInvariant());
                var fac = GetFactionFromColor(child.gameObject);
                counts[(int)fac, (int)cat]++;

                var img = child.gameObject.GetComponent<Image>();
                string col = img != null
                    ? $"r={img.color.r:F2} g={img.color.g:F2} b={img.color.b:F2}"
                    : "no Image";
                Log.LogInfo($"  [{fac}/{cat}] \"{name}\"  color=({col})");
            }

            Log.LogInfo("");
            Log.LogInfo("  --- Totals ---");
            foreach (var fac in AllFactions)
                foreach (var cat in AllCategories)
                    if (counts[(int)fac, (int)cat] > 0)
                        Log.LogInfo($"    {fac} / {cat}: {counts[(int)fac, (int)cat]}");

            Log.LogInfo("");
            Log.LogInfo("  --- Unclassified (Other) icons currently spawned: ---");
            foreach (Transform child in layer)
            {
                string name = child.gameObject.name;
                if (ShouldIgnore(name)) continue;
                if (Classify(name.ToLowerInvariant()) == IconCategory.Other)
                    Log.LogInfo($"    UNCLASSIFIED: \"{name}\"");
            }

            Log.LogInfo("=== HUD ICON TOGGLE: DUMP END ===");
        }

        // Dumps the display name of every unit/building "Parameters" or
        // "Definition" ScriptableObject currently loaded in memory, regardless
        // of whether it's spawned in a mission. Can be run from the main menu.
        private void DumpAllUnitDefinitions()
        {
            Log.LogInfo("=== HUD ICON TOGGLE: ALL UNIT DEFINITIONS ===");

            var allObjects = Resources.FindObjectsOfTypeAll<ScriptableObject>();
            Log.LogInfo($"  Total ScriptableObjects in memory: {allObjects.Length}");

            // Collect all unique type names first so we can see what's available
            var typeNames = new SortedSet<string>();
            foreach (var obj in allObjects)
                typeNames.Add(obj.GetType().Name);

            Log.LogInfo("  --- All ScriptableObject type names found: ---");
            foreach (var tn in typeNames)
                Log.LogInfo($"    {tn}");

            Log.LogInfo("  --- Entries matching 'Parameters' or 'Definition': ---");
            int total = 0;
            var unclassified = new List<string>();

            foreach (var obj in allObjects)
            {
                string typeName = obj.GetType().Name;
                if (!typeName.Contains("Parameters") && !typeName.Contains("Definition"))
                    continue;

                string displayName = GetDisplayName(obj) ?? obj.name;
                var cat = Classify(displayName.ToLowerInvariant());
                Log.LogInfo($"  [{typeName}] [{cat}] \"{displayName}\"  (asset: \"{obj.name}\")");
                total++;

                if (cat == IconCategory.Other)
                    unclassified.Add(displayName);
            }

            Log.LogInfo($"  --- Total matching definitions: {total} ---");
            Log.LogInfo("  --- Unclassified (Other): ---");
            foreach (var n in unclassified)
                Log.LogInfo($"    UNCLASSIFIED: \"{n}\"");

            Log.LogInfo("=== END ALL UNIT DEFINITIONS ===");
        }

        // Reflection helper: looks for a human-readable display-name field/property.
        // Results are cached per Type so identical definition types only pay the
        // reflection cost once across the BuildTypeMap scan.
        private static readonly Dictionary<Type, System.Reflection.MemberInfo> s_displayNameCache
            = new Dictionary<Type, System.Reflection.MemberInfo>();

        private static readonly string[] s_displayNameCandidates
            = { "displayName", "DisplayName", "unitName", "UnitName" };

        private static readonly System.Reflection.BindingFlags s_bfInstance
            = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        private static string GetDisplayName(object obj)
        {
            var type = obj.GetType();

            if (!s_displayNameCache.TryGetValue(type, out var member))
            {
                member = null;
                foreach (var name in s_displayNameCandidates)
                {
                    var f = type.GetField(name, s_bfInstance);
                    if (f != null && f.FieldType == typeof(string)) { member = f; break; }
                    var p = type.GetProperty(name, s_bfInstance);
                    if (p != null && p.PropertyType == typeof(string)) { member = p; break; }
                }
                s_displayNameCache[type] = member; // cache even if null
            }

            if (member is System.Reflection.FieldInfo fi)
            {
                var val = fi.GetValue(obj) as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            else if (member is System.Reflection.PropertyInfo pi)
            {
                var val = pi.GetValue(obj) as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }

            return null;
        }

        private void LogKeybinds()
        {
            // Keybind logging removed: all toggle input is now Rewired/ExtraInputManager.
            // Keep this method so existing call-sites don't break.
            Log.LogDebug($"HUD Icon Toggle v{VERSION} — toggle input via Rewired actions (config checkboxes only for visibility).");
        }

        private static string GetPath(Transform t)
        {
            var parts = new Stack<string>();
            while (t != null) { parts.Push(t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        // ── Data types ────────────────────────────────────────────────────────

        private class IconEntry
        {
            public readonly GameObject   Go;
            public readonly IconFaction  Faction;
            public readonly IconCategory Category;
            public IconEntry(GameObject go, IconFaction fac, IconCategory cat)
            { Go = go; Faction = fac; Category = cat; }
        }
    }

public enum IconCategory { Aircraft, Buildings, Ground, Missiles, Naval, Other }
    public enum IconFaction   { Friendly, Enemy, Neutral }

    // ── ConfigurationManagerAttributes stub ───────────────────────────────────
    // Copied inline so we don't need a hard reference to the Configuration Manager
    // DLL. When the mod is absent the attributes are silently ignored by BepInEx;
    // when it is present it picks up these properties via reflection and uses them
    // to control ordering, descriptions, and read-only display in the F1 menu.
#pragma warning disable CS0649 // Fields set via reflection by Configuration Manager
    internal sealed class ConfigurationManagerAttributes
    {
        public bool?   Browsable;
        public bool?   ReadOnly;
        public bool?   IsAdvanced;
        public int?    Order;
        public string  Category;
        public string  DispName;
        public string  Description;
        public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
    }
#pragma warning restore CS0649
}
