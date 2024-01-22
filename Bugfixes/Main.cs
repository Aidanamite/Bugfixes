using BepInEx;
using ConfigTweaks;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using System.Threading;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using System.Diagnostics;
using System.Globalization;

namespace Bugfixes
{
    [BepInPlugin("com.aidanamite.Bugfixes", "Client Bugfixes", "1.0.9")]
    [BepInDependency("com.aidanamite.ConfigTweaks", "1.1.0")]
    public class Main : BaseUnityPlugin
    {
        [ConfigField]
        public static KeyCode ForceInteractable = KeyCode.KeypadMultiply;
        [ConfigField]
        public static bool FixGrowthUI = true;
        [ConfigField]
        public static bool DisplayDragonGender = true;
        [ConfigField(Description = "DEV PURPOSES ONLY: This can take a long time to load (the game will be frozen during this time) and may have significant performance impact while active")]
        public static bool EnableLagSpikeProfiling = false;
        [ConfigField]
        public static long LagThreashold = 200000;
        [ConfigField]
        public static double LagDisplayThreashold = 0.5;

        public static BepInEx.Logging.ManualLogSource LogSource;
        bool TestPatchMethod() => false;
        static IEnumerable<CodeInstruction> TestPatch(IEnumerable<CodeInstruction> instructions) => new[] { new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Ret) };
        public void Awake()
        {
            try
            {
                var target = AccessTools.Method(typeof(Main), nameof(TestPatchMethod));
                new Harmony("com.aidanamite.Bugfixes.TestPatch").Patch(target, transpiler: new HarmonyMethod(AccessTools.Method(typeof(Main), nameof(TestPatch))));
                if (!(bool)target.Invoke(this, new object[0]))
                {
                    errMsg = "There was an unknown issue loading mods";
                    Logger.LogError("\n\n=================================================================\n===============                                   ===============\n===============  PATCH FAILED WITHOUT EXCEPTION!  ===============\n===============       this may cause issues       ===============\n===============                                   ===============\n=================================================================\n");
                }
            }
            catch
            {
                bool invalid = false;
                foreach (var c in Environment.CurrentDirectory)
                    if (c > 127)
                    {
                        invalid = true;
                        errMsg = "There was an error loading mods which may be caused by invalid letters/symbols in the file path. Try moving the game into another folder";
                        Logger.LogError("\n\n==================================================================\n===============                                    ===============\n===============    PATCH FAILED WITH EXCEPTION!    ===============\n===============     this is probably caused by     ===============\n===============     invalid chars in game path     ===============\n===============                                    ===============\n==================================================================\n");
                        break;
                    }
                if (!invalid)
                {
                    errMsg = "There was an unknown error loading mods";
                    Logger.LogError("\n\n==================================================================\n===============                                    ===============\n===============    PATCH FAILED WITH EXCEPTION!    ===============\n===============    the cause of this is unknown    ===============\n===============                                    ===============\n==================================================================\n");
                }
            }
            LogSource = Logger;
            new Harmony("com.aidanamite.Bugfixes").PatchAll();
            Logger.LogInfo("Loaded");
        }

        string errMsg;

        public void OnDestroy()
        {
            // Fixes a bug where trying to close the game would simply make it stop responding
            Process.GetCurrentProcess().Kill();
        }

        public static bool AllowUIUpdate = true;
        int updateStep;
        public void Update()
        {
            if (Input.GetKeyDown(ForceInteractable)) // A workaround for UIs not being interactable when they should've been. Sometimes there's still issues after getting out of the UI
                foreach (var i in FindObjectsOfType<KAUI>())
                    if (i.GetVisibility())
                        i.SetInteractive(true);

            updateStep = (updateStep + 1) % ((int)(Patch_Widget.count * 11L / 80000) + 1);
            AllowUIUpdate = updateStep == 0;

            if (errMsg != null && UiLogin.pInstance)
            {
                GameUtilities.DisplayOKMessage("PfKAUIGenericDB", errMsg, null, "");
                errMsg = null;
            }
            if (Patch_UpdateProfiling.total > 0)
            {
                var v = Patch_UpdateProfiling.recorded;
                Patch_UpdateProfiling.recorded = new Dictionary<MethodBase, (long,int)>();
                var t = Patch_UpdateProfiling.total;
                Patch_UpdateProfiling.total = 0;
                if (t >= LagThreashold)
                {
                    var sort = new SortedList<long, (MethodBase,int)>(new SortProfile());
                    foreach (var p in v)
                        sort.Add(p.Value.Item1, (p.Key,p.Value.Item2));
                    var s = new StringBuilder();
                    s.Append("Frame took ");
                    s.Append(t / 10000);
                    s.Append("ms");
                    var subtotal = 0L;
                    var threshold = 0L;
                    foreach (var p in sort)
                    {
                        if (threshold == 0)
                            threshold = (long)(p.Key * LagDisplayThreashold);
                        if (p.Key < threshold)
                            break;
                        subtotal += p.Key;
                        s.Append("\n[");
                        if (p.Key > 100000)
                            s.Append(p.Key / 10000);
                        else
                            s.Append(Math.Round(p.Key / 10000.0,2));
                        if (p.Value.Item2 != 1)
                        {
                            s.Append(" | ");
                            s.Append(p.Value.Item2);
                        }
                        s.Append("] ==== ");
                        s.Append(p.Value.Item1.DeclaringType.FullName);
                        s.Append("::");
                        s.Append(p.Value.Item1);
                    }
                    s.Append("\nDisplayed values total to ");
                    s.Append(subtotal / 10000);
                    s.Append("ms");
                    Logger.LogWarning(s.ToString());
                }
            }
            if (EnableLagSpikeProfiling && !appliedProfiling)
            {
                appliedProfiling = true;
                Patch_UpdateProfiling.main = Thread.CurrentThread;
                var h = new Harmony("com.aidanamite.Bugfixes.Profiling");
                var prefix = new HarmonyMethod(typeof(Patch_UpdateProfiling).GetMethod(nameof(Patch_UpdateProfiling.Prefix)));
                var final = new HarmonyMethod(typeof(Patch_UpdateProfiling).GetMethod(nameof(Patch_UpdateProfiling.Finalizer)));
                var c = 0;
                foreach (var m in Patch_UpdateProfiling.TargetMethods())
                    try
                    {
                        c++;
                        if (c % 100 == 0)
                            Logger.LogInfo($"Added profile patch to {c} methods and counting");
                        h.Patch(m, prefix: prefix, finalizer: final);
                    } catch (Exception e)
                    {
                        Logger.LogError(e);
                    }
                Logger.LogInfo($"Applied {c} profiling patches");
            }
        }

        bool appliedProfiling = false;

        class SortProfile : IComparer<long>
        {
            int IComparer<long>.Compare(long x, long y)
            {
                var r = y.CompareTo(x);
                return r == 0 ? 1 : r;
            }
        }

        public static void GetDetailedString(StackTrace t, StringBuilder s)
        {
            var first = true;
            foreach (var f in t.GetFrames())
            {
                if (first)
                    first = false;
                else
                    s.Append("\n");
                s.Append(" at ");
                var m = f.GetMethod();
                var isDynamic = false;
                if (m == null)
                    s.Append("(unknown method");
                else
                {
                    if (m is MethodInfo m2)
                        s.Append(m2.ReturnType);
                    else
                        s.Append(m.MemberType);
                    s.Append(" ");
                    isDynamic = m.DeclaringType == null;
                    if (isDynamic)
                        s.Append("(dynamic method) ");
                    else
                    {
                        s.Append(m.DeclaringType);
                        s.Append(".");
                    }
                    s.Append(m.Name);
                    try
                    {
                        var g = m.GetGenericArguments();
                        if (g != null && g.Length > 0)
                        {
                            s.Append("`");
                            s.Append(g.Length);
                            s.Append("[");
                            var i = true;
                            foreach (var a in g)
                            {
                                if (i)
                                    i = false;
                                else
                                    s.Append(",");
                                s.Append(a);
                            }
                            s.Append("]");
                        }
                    }
                    catch { }
                    s.Append("(");
                    var p = m.GetParameters();
                    var l = true;
                    foreach (var a in p)
                    {
                        if (l)
                            l = false;
                        else
                            s.Append(", ");
                        s.Append(a.ParameterType);
                        if (a.IsOut)
                            s.Append("&");
                        s.Append(" ");
                        s.Append(a.Name);
                    }
                }
                s.Append(") [0x");
                s.Append(f.GetILOffset().ToString("X"));
                s.Append("] [");
                if (m == null || isDynamic)
                    s.Append("???");
                else
                {
                    try
                    {
                        var func = m.MethodHandle.GetFunctionPointer().ToInt64().ToString("X");
                        s.Append("0x");
                        s.Append(func);
                    }
                    catch
                    {
                        s.Append("???");
                    }
                }
                s.Append("+");
                s.Append(f.GetNativeOffset().ToString("X"));
                s.Append("] <");
                s.Append(f.GetFileName() ?? "unknown file");
                s.Append(":");
                s.Append(f.GetFileLineNumber());
                s.Append(":");
                s.Append(f.GetFileColumnNumber());
                s.Append(">");
            }
        }
    }

    static class ExtentionMethods
    {
        public static KAWidget GetEmptyIncubatorSlot(this UiHatchingSlotsMenu menu)
        {
            List<KAWidget> items = menu.GetItems();
            if (items != null && items.Count > 0)
                for (int i = 0; i < items.Count; i++)
                {
                    var widget = (IncubatorWidgetData)items[i].GetUserData();
                    if (widget != null && widget.Incubator)
                    {
                        Debug.Log($"Incubator {widget.Incubator} is in state {widget.Incubator.pMyState}");
                        if (widget.Incubator.pMyState <= Incubator.IncubatorStates.IDLE)
                            return items[i];
                    }
                }
            return null;
        }
        public static string GetString(this Object obj)
        {
            if (obj)
                return obj.ToString();
            else if ((object)obj == null)
                return "{NULL}";
            else
                return "{DESTROYED}";
        }
        public static string GetOperandString(this object obj, List<CodeInstruction> code = null) => obj == null ? "" : $"{(obj is Label l ? code == null ? -2 : code.FindIndex(y => y.labels != null && y.labels.Contains(l)) : obj is MemberInfo m && m.DeclaringType != null ? m.DeclaringType.FullName + "::" + m : obj)} [{obj.GetType().FullName}]";

        static FieldInfo _locals = typeof(ILGenerator).GetField("locals", ~BindingFlags.Default);
        public static LocalBuilder[] GetLocals(this ILGenerator generator) => (LocalBuilder[])_locals.GetValue(generator);

        static FieldInfo _mDragonMale = typeof(UiDragonCustomization).GetField("mDragonMale", ~BindingFlags.Default);
        public static bool IsMale(this UiDragonCustomization ui) => (bool)_mDragonMale.GetValue(ui);
        public static void IsMale(this UiDragonCustomization ui, bool newValue) => _mDragonMale.SetValue(ui, newValue);
        static FieldInfo _mToggleBtnMale = typeof(UiDragonCustomization).GetField("mToggleBtnMale", ~BindingFlags.Default);
        public static KAToggleButton GetButtonMale(this UiDragonCustomization ui) => (KAToggleButton)_mToggleBtnMale.GetValue(ui);
        static FieldInfo _mToggleBtnFemale = typeof(UiDragonCustomization).GetField("mToggleBtnFemale", ~BindingFlags.Default);
        public static KAToggleButton GetButtonFemale(this UiDragonCustomization ui) => (KAToggleButton)_mToggleBtnFemale.GetValue(ui);
        static FieldInfo _StartChecked = typeof(KAToggleButton).GetField("_StartChecked", ~BindingFlags.Default);
        public static void SetStartChecked(this KAToggleButton ui, bool value) => _StartChecked.SetValue(ui,value);

        static MethodInfo _pGamePlayTime = typeof(SquadTactics.GameManager).GetProperty("pGamePlayTime", ~BindingFlags.Default).GetSetMethod(true);
        public static void SetGamePlayTime(this SquadTactics.GameManager manager, float value) => _pGamePlayTime.Invoke(manager,new object[] { value });
    }

    // Fixes a bug where the server would double send a mission complete message that causes an error that softlocks the game
    [HarmonyPatch(typeof(MissionManager), "MissionCompleteCallback")] 
    static class Patch_MissionComplete
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].operand is MethodInfo m && m.DeclaringType.IsConstructedGenericType && m.Name == "Add" && typeof(Dictionary<,>) == m.DeclaringType.GetGenericTypeDefinition())
                    code[i].operand = AccessTools.Method(m.DeclaringType, "set_Item");
            return code;
        }
    }

    // An attempt to fix certain music being treated like general sound effects
    [HarmonyPatch] 
    static class Patch_SoundGroup
    {
        static HashSet<(string, PoolGroup)> found = new HashSet<(string, PoolGroup)>();
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SnChannel), "ApplySettings");
            yield return AccessTools.Method(typeof(SnChannel), "AddChannel", new[] { typeof(SnChannel) });
            yield return AccessTools.Method(typeof(SnChannel), "SetVolumeForPoolGroup");
            yield return AccessTools.Method(typeof(SnChannel), "TurnOffPools");
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f && f.Name == "_Group")
                    code.Insert(i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_SoundGroup), nameof(CheckGroup))));
            return code;
        }
        static PoolInfo CheckGroup(PoolInfo instance)
        {
            if (instance._Name == "AmbSFX_Pool")
                instance._Group = PoolGroup.MUSIC;
            //if (found.Add((instance._Name, instance._Group)))
                //Main.LogSource.LogInfo($"Pool info {instance._Group} \"{instance._Name}\"");
            return instance;
        }
    }

    // Some extra logging to try and figure out the cause of the "accept mission" ui sometimes not becomming interactable and softlocking the game
    [HarmonyPatch(typeof(UtDebug), "LogWarning",typeof(object),typeof(int))] 
    static class Patch_LogWarning
    {
        static void Prefix(ref object message)
        {
            if (message is string str && str.StartsWith("FindItem can't find item "))
            {
                var n = new StringBuilder();
                n.Append(str);
                n.Append("\n");
                Main.GetDetailedString(new StackTrace(1, true), n);
                message = n.ToString();
            }
        }
    }

    // Fixes a bug where certain objects had a null/destroyed path object which would cause the entire pathing to fail and the object to not move at all
    [HarmonyPatch] 
    static class Patch_SplineFix
    {
        static MethodBase TargetMethod() => typeof(NPCSplineMove).GetMethods(~BindingFlags.Default).First(x => x.Name.StartsWith("<StartMove>"));
        static bool Prefix(NPCSplineMove __instance,ref bool __result)
        {
            if (!__instance.pathContainer)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // Fixes a bug where putting an egg into the hatchery *after* completing a tutorial in the same area would cause a UI bug
    [HarmonyPatch(typeof(Incubator), "CheckEggSelected")] 
    static class Patch_CheckEggSelected
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code.Insert(
                code.FindIndex(
                    code.FindIndex(x => x.opcode == OpCodes.Ldsfld && x.operand is FieldInfo f && f.Name == "_CurrentActiveTutorialObject"),
                    x => x.opcode == OpCodes.Dup) + 1,
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_CheckEggSelected), nameof(CheckDestroyed))));
            return code;
        }
        static bool CheckDestroyed(GameObject obj) => obj;
    }

    // Fixes a bug with some mission reward popups softlocking the game
    [HarmonyPatch(typeof(RewardWidget), "AddRewardItem")] 
    static class Patch_AddRewards
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code[code.FindIndex(x => x.opcode == OpCodes.Ldelem)] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_AddRewards), nameof(GetItemSafe)));
            return code;
        }
        static Vector2 GetItemSafe(Vector2[] array, int index) => array.Length > index ? array[index] : default;
    }

    // An attempt to fix a bug where putting an egg into the hatchery would cause a UI bug. This did not work but for now the bug has stopped occuring so further diagnosis is difficult
    /*[HarmonyPatch(typeof(UiHatchingSlotsMenu), "OnEggPlaced")] 
    static class Patch_HatchingUIAddEgg
    {
        static void Prefix(UiHatchingSlotsMenu __instance, ref KAWidget ___mInteractingWidget)
        {
            if (!___mInteractingWidget && !(___mInteractingWidget = __instance.GetEmptyIncubatorSlot()))
                Debug.LogError("Failed to fix target slot (UiHatchingSlotsMenu::OnEggPlaced)");
        }
    }*/

    // Fixes a bug where the dragon age up pop up could open on top of another ui
    [HarmonyPatch(typeof(ObProximityHatch), "Update")] 
    static class Patch_GrowUI
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code.InsertRange(0, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_GrowUI), nameof(CheckTime)))
            });
            code.InsertRange(code.FindIndex(x => x.opcode == OpCodes.Ldsfld && x.operand is FieldInfo f && f.Name == "pCurPetInstance") + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_GrowUI), nameof(CheckOpenAgeUpUI)))
            });
            return code;
        }

        static ConditionalWeakTable<ObProximityHatch, Memory> memory = new ConditionalWeakTable<ObProximityHatch, Memory>();
        static SanctuaryPet CheckOpenAgeUpUI(SanctuaryPet original, ObProximityHatch instance) => !Main.FixGrowthUI || (AvAvatar.pInputEnabled && AvAvatar.pState != AvAvatarState.PAUSED && AvAvatar.pState != AvAvatarState.NONE && memory.GetOrCreateValue(instance).lifetime > 5) ? original : null;
        static void CheckTime(ObProximityHatch instance)
        { // Still needs testing
            if (AvAvatar.pInputEnabled && AvAvatar.pState != AvAvatarState.PAUSED && AvAvatar.pState != AvAvatarState.NONE)
                memory.GetOrCreateValue(instance).lifetime += Time.deltaTime;
        }
        class Memory
        {
            public float lifetime;
        }
    }


    // Fixes a bug with the age up prompt where the player's control would not be restored when closing the ui
    [HarmonyPatch(typeof(DragonAgeUpConfig), "ShowAgeUpUI", typeof(DragonAgeUpConfig.OnDragonAgeUpDone), typeof(RaisedPetStage), typeof(RaisedPetData), typeof(RaisedPetStage[]), typeof(bool), typeof(bool), typeof(GameObject), typeof(string))]
    static class Patch_ShowAgeUpUI_SimpleToComplex
    {
        static bool Prefix(DragonAgeUpConfig.OnDragonAgeUpDone inOnDoneCallback, RaisedPetStage fromStage, RaisedPetData inData, RaisedPetStage[] requiredStages, bool ageUpDone, bool isUnmountableAllowed, GameObject messageObj, string assetName)
        {
            if (inOnDoneCallback != null)
            {
                DragonAgeUpConfig.ShowAgeUpUI(() => inOnDoneCallback(), inOnDoneCallback, () => inOnDoneCallback(), fromStage, inData, requiredStages, ageUpDone, isUnmountableAllowed, messageObj, assetName);
                return false;
            }
            return true;
        }
    }

    // Fixes a bug where the npc quest dialog will sometimes never become interactable. While not ideal, it currently works by stopping it being disabled in the first place
    [HarmonyPatch(typeof(UiNPCQuestDetails), "SetupDetailsUi")]
    static class Patch_SetupQuestUI
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code[code.FindLastIndex(x => x.operand is MethodInfo m && m.Name == "SetState") - 1].opcode = OpCodes.Ldc_I4_0;
            return code;
        }
    }

    // Main component of enabling the dragon's gender to be displayed next to it's species. This adds a call to the TryReplace function just after loading the _NameText value along with the pet instance being displayed
    [HarmonyPatch]
    static class Patch_DisplayDragonGender
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var l = new List<MethodBase>();
            foreach (var t in new[] {
                typeof(JSGames.UI.Util.UIUtil),
                typeof(KAUISelectDragonMenu),
                typeof(UiChooseADragon),
                typeof(UiDragonCustomization),
                typeof(UiDragonsAgeUpMenuItem),
                typeof(UiDragonsInfoCardItem),
                typeof(UiDragonsListCard),
                typeof(UiDragonsListCardMenu),
                typeof(UiMessageInfoUserData),
                typeof(UiMOBASelectDragon),
                typeof(UiSelectHeroDragons),
                typeof(UiStableQuestCompleteMenu),
                typeof(UiStableQuestDragonSelect),
                typeof(WsUserMessage)
            })
                foreach (var m in t.GetMethods(~BindingFlags.Default))
                    if (!m.IsGenericMethodDefinition)
                        try
                        {
                            foreach (var i in PatchProcessor.GetCurrentInstructions(m))
                                if (i.opcode == OpCodes.Ldfld && i.operand is FieldInfo f && f.Name == "_NameText" && f.DeclaringType == typeof(SanctuaryPetTypeInfo))
                                {
                                    l.Add(m);
                                    break;
                                }
                        }
                        catch { }
            return l;
        }
        static bool CorrectType(Type type, MethodBase method) => typeof(RaisedPetData).IsAssignableFrom(type) || typeof(SanctuaryPet).IsAssignableFrom(type) || typeof(HeroPetData).IsAssignableFrom(type) || typeof(KAUISelectDragon).IsAssignableFrom(type) || (method.Name == "ReplaceTagWithPetData" && typeof(RewardData).IsAssignableFrom(type));
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator iL)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f && f.Name == "_NameText" && f.DeclaringType == typeof(SanctuaryPetTypeInfo))
                {
                    var ins = new List<CodeInstruction>() { new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_DisplayDragonGender), nameof(TryReplace))) };
                    var flag = false;
                    var stacksize = 1;
                    var hasFallback = false;
                    for (int j = i - 1; j >= 0; j--)
                    {
                        if (!flag)
                        {
                            Type t = null;
                            if (code[j].operand is FieldInfo f2)
                                t = f2.FieldType;
                            else if (code[j].operand is MethodInfo m && m.GetParameters().Length == 0)
                                t = m.ReturnType;
                            else if (code[j].operand is PropertyInfo p && p.GetIndexParameters().Length == 0)
                                t = p.PropertyType;
                            else if (code[j].opcode.ToString().ToLowerInvariant().StartsWith("ldarg"))
                            {
                                var ind =
                                    code[j].opcode == OpCodes.Ldarg_0
                                    ? 0
                                    : code[j].opcode == OpCodes.Ldarg_1
                                    ? 1
                                    : code[j].opcode == OpCodes.Ldarg_2
                                    ? 2
                                    : code[j].opcode == OpCodes.Ldarg_3
                                    ? 3
                                    : code[j].operand is ParameterBuilder arg
                                    ? arg.Position + (method.IsStatic ? 0 : 1)
                                    : code[j].operand is IConvertible con
                                    ? con.ToInt32(CultureInfo.InvariantCulture)
                                    : 0;
                                if (!method.IsStatic)
                                    ind--;
                                if (ind == -1)
                                    t = method.DeclaringType;
                                else
                                    t = method.GetParameters()[ind].ParameterType;
                            }
                            else if (code[j].operand is LocalBuilder loc && code[j].opcode.ToString().ToLowerInvariant().StartsWith("ld"))
                                t = loc.LocalType;
                            if (CorrectType(t, method))
                                flag = true;
                        }
                        if (flag)
                        {
                            ins.Insert(0, new CodeInstruction(code[j]));
                            if (ins[0].operand is FieldInfo f2)
                            {
                                if (ins[0].opcode.ToString().ToLowerInvariant()[0] == 'l')
                                    stacksize--;
                                else
                                    stacksize++;
                                if (!f2.IsStatic)
                                    stacksize++;
                            }
                            else if (ins[0].operand is MethodInfo m)
                            {
                                stacksize += m.GetParameters().Length;
                                if (!m.IsStatic)
                                    stacksize++;
                                if (m.ReturnType != typeof(void) && m.ReturnType != null)
                                    stacksize--;
                            }
                            else
                            {
                                if (ins[0].opcode.StackBehaviourPop != StackBehaviour.Pop0 && ins[0].opcode.StackBehaviourPop != StackBehaviour.Varpop)
                                    stacksize += ins[0].opcode.StackBehaviourPop.ToString().ToLowerInvariant().Split(new[] { "pop" }, StringSplitOptions.None).Length - 1;
                                if (ins[0].opcode.StackBehaviourPush != StackBehaviour.Push0 && ins[0].opcode.StackBehaviourPush != StackBehaviour.Varpush)
                                    stacksize -= ins[0].opcode.StackBehaviourPush.ToString().ToLowerInvariant().Split(new[] { "push" }, StringSplitOptions.None).Length - 1;
                            }
                            // Some debugging code for the stack detection used
                            //Debug.Log($"Getting values: (stack={stacksize},il=(opcode={ins[0].opcode}{(ins[0].operand != null ? ",operand=" + ins[0].operand.GetOperandString() : "")}))");
                            if (stacksize == 0)
                                break;
                            if (j == 0)
                                flag = false;
                        }
                        if (!hasFallback && code[j].operand is MethodInfo m2 && Patch_GetPetFallback.fallbacks.Contains(m2))
                            hasFallback = true;
                    }
                    if (!flag && ins.Count > 1)
                        ins.RemoveRange(0, ins.Count - 1);
                    if (!flag && hasFallback)
                    {
                        flag = true;
                        ins.Insert(0, new CodeInstruction(OpCodes.Ldsfld, typeof(Patch_GetPetFallback).GetField(nameof(Patch_GetPetFallback.recent))));
                    }
                    if (!flag)
                    {
                        foreach (var f2 in method.DeclaringType.GetFields(~BindingFlags.Default))
                            if (CorrectType(f2.FieldType, method) && (f2.IsStatic || !method.IsStatic))
                            {
                                flag = true;
                                ins.Insert(0, new CodeInstruction(f2.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, f2));
                                if (!f2.IsStatic)
                                    ins.Insert(0, new CodeInstruction(OpCodes.Ldarg_0));
                                break;
                            }
                    }
                    if (!flag)
                        foreach (var p in method.DeclaringType.GetProperties(~BindingFlags.Default))
                            if (p.GetGetMethod(true) != null && CorrectType(p.PropertyType, method) && (p.GetGetMethod(true).IsStatic || !method.IsStatic))
                            {
                                flag = true;
                                ins.Insert(0, new CodeInstruction(p.GetGetMethod(true).IsStatic ? OpCodes.Call : OpCodes.Callvirt, p.GetGetMethod(true)));
                                if (!p.GetGetMethod(true).IsStatic)
                                    ins.Insert(0, new CodeInstruction(OpCodes.Ldarg_0));
                                break;
                            }
                    if (flag)
                        code.InsertRange(i + 1, ins);
                    else
                    {
                        var k = 0;
                        Debug.LogWarning($"Failed to patch operation at {i} in {method.DeclaringType.FullName}::{method}{code.Join(x => $"\n{k++}| {x.opcode}{x.operand.GetOperandString(code)}")}");
                    }
                }
            return code;
        }
        static ConditionalWeakTable<object, ConditionalWeakTable<LocaleString, AppendedLocaleString>> cache = new ConditionalWeakTable<object, ConditionalWeakTable<LocaleString, AppendedLocaleString>>();
        static LocaleString TryReplace(LocaleString str, object pet)
        {
            if (!Main.DisplayDragonGender)
                return str;
            var g = pet is RaisedPetData raised ? raised.Gender : pet is HeroPetData hero ? hero._Gender : pet is SanctuaryPet sanctuary ? sanctuary.pData.Gender : pet is KAUISelectDragon selectDragon ? selectDragon.pPetData.Gender : pet is RewardData reward ? RaisedPetData.GetByEntityID(new Guid(reward.EntityID)).Gender : Gender.Unknown;
            if (g == Gender.Unknown)
                return str;
            return new AppendedLocaleString(g + " ", str, "");
        }
    }

    // Custom class to act as an override of the normal locale string behaviour
    class AppendedLocaleString : LocaleString
    {
        public LocaleString original;
        public string prefix;
        public string postfix;
        public AppendedLocaleString(string Prefix, LocaleString Original, string Postfix) : base(null)
        {
            prefix = Prefix ?? "";
            original = Original;
            postfix = Postfix ?? "";
        }
        public string GetOverrideString() => string.Concat(prefix, original.GetLocalizedString(), postfix);
    }

    // Makes the custom locale string class work
    [HarmonyPatch(typeof(LocaleString), "GetLocalizedString")]
    static class Patch_GetLocalizedString
    {
        static bool Prefix(LocaleString __instance, ref string __result)
        {
            if (__instance is AppendedLocaleString a)
            {
                __result = a.GetOverrideString();
                return false;
            }
            return true;

        }
    }

    // Makes the UI update when changing the dragon's gender so that the gender display in the hatching UI doesn't just say "Male" the whole time
    [HarmonyPatch(typeof(UiDragonCustomization),"OnClick")]
    static class Patch_UpdateCustomization
    {
        static void Postfix(UiDragonCustomization __instance, KAWidget inItem, ref bool ___mUiRefresh, KAToggleButton ___mToggleBtnFemale, KAToggleButton ___mToggleBtnMale, bool ___mDragonMale)
        {
            if (Main.DisplayDragonGender && (inItem == ___mToggleBtnMale || inItem == ___mToggleBtnFemale))
            {
                __instance.pPetData.Gender = ___mDragonMale ? Gender.Male : Gender.Female;
                ___mUiRefresh = true;
            }
        }
    }

    // Used by the dragon gender display in situations where a set of IL instructions could not be found to append the current pet to the stack.
    // This will remember the last pet instance returned by 2 functions which is then used in place of existing IL
    [HarmonyPatch]
    static class Patch_GetPetFallback
    {
        public static MethodBase[] fallbacks =
        {
            AccessTools.Method(typeof(RaisedPetData),"GetByEntityID"),
            AccessTools.Method(typeof(RaisedPetData),"GetByID")
        };
        static IEnumerable<MethodBase> TargetMethods() => fallbacks;
        public static RaisedPetData recent;
        static void Postfix(RaisedPetData __result) => recent = __result;
    }

    // Fixes a bug where the dragon's gender would be reset to Male after customizing your dragon post-hatch
    [HarmonyPatch(typeof(KAUISelectDragon), "set_pPetData")]
    static class Patch_SetSelectedPet
    {
        static void Postfix(KAUISelectDragon __instance)
        {
            if (__instance is UiDragonCustomization ui)
                ui.IsMale(ui.pPetData.Gender == Gender.Male);
        }
    }

    // Fixes a bug where the default enabled gender button was not the same as the current dragon's gender
    [HarmonyPatch(typeof(UiDragonCustomization), "Initialize")]
    static class Patch_InitDragonCustomization
    {
        static void Postfix(UiDragonCustomization __instance, KAToggleButton ___mToggleBtnFemale, KAToggleButton ___mToggleBtnMale)
        {
            if (___mToggleBtnMale)
            {
                ___mToggleBtnMale.SetChecked(__instance.IsMale());
                ___mToggleBtnMale.SetStartChecked(__instance.IsMale());
            }
            if (___mToggleBtnFemale)
            {
                ___mToggleBtnFemale.SetChecked(!__instance.IsMale());
                ___mToggleBtnFemale.SetStartChecked(!__instance.IsMale());
            }
        }
    }

    // Fixes a bug where you could click on battle objects underneath UI buttons sometimes causing unintended actions
    [HarmonyPatch(typeof(SquadTactics.GameManager),"Update")]
    static class Patch_SquadGameUpdate
    {
        static bool MouseDown = false;
        static bool Prefix(SquadTactics.GameManager __instance)
        {
            if (MouseDown)
            {
                if (Input.GetMouseButtonUp(0))
                    MouseDown = false;
            }
            else if (Input.GetMouseButtonDown(0) && KAUI.GetGlobalMouseOverItem())
                MouseDown = true;
            else
                return true;
            if (__instance._GameState != SquadTactics.GameManager.GameState.GAMEOVER)
                __instance.SetGamePlayTime(__instance.pGamePlayTime + Time.deltaTime);
            return false;
        }
    }

    // Fixes some FPS issues in Dragon Tactics caused by use of GameObject.Find
    [HarmonyPatch(typeof(UIButtonProcessor),"Update")]
    static class Patch_ButtonFind
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex(x => x.operand is MethodInfo m && m.Name == "Find" && m.DeclaringType == typeof(GameObject));
            code.RemoveAt(ind);
            code.InsertRange(ind, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Patch_ButtonFind),nameof(CachedFind)))
            });
            return code;
        }
        static ConditionalWeakTable<object, Dictionary<string, GameObject>> table = new ConditionalWeakTable<object, Dictionary<string, GameObject>>();
        static GameObject CachedFind(string name, object self)
        {
            if (table.GetOrCreateValue(self).TryGetValue(name, out var go) && go)
                return go;
            return table.GetOrCreateValue(self)[name] = GameObject.Find(name);
        }
    }

    // Fixes some FPS issues in Dragon Tactics caused the by the game loading the battle backpack for some unknown reason
    [HarmonyPatch(typeof(KAUISelectMenu), "FinishMenuItems")]
    static class Patch_PreventItemMenuLoad
    {
        static bool Prefix() => !SquadTactics.GameManager.pInstance;
    }

    // Allows limiting of the UI framerate based on the number of UI objects present. AllowUIUpdate is updated in Main.Update
    [HarmonyPatch(typeof(KAWidget))]
    static class Patch_Widget
    {
        public static int count;
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        static void Awake() => count++;
        [HarmonyPatch("OnDestroy")]
        [HarmonyPrefix]
        static void Destroy() => count--;
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        static bool Update() => Main.AllowUIUpdate;
    }

    static class Patch_UpdateProfiling
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var a = typeof(SanctuaryManager).Assembly;
            foreach (var t in a.GetTypes())
                if (!t.ContainsGenericParameters && t.Assembly == a)
                    foreach (var m in t.GetMethods(~BindingFlags.Default))
                        if (!m.ContainsGenericParameters && m.Name.Contains("Update") && m.IsDeclaredMember() && m.HasMethodBody() && m.DeclaringType == t)
                            yield return m;
            yield break;
        }
        public static Thread main;
        public static Dictionary<MethodBase, (long, int)> recorded = new Dictionary<MethodBase, (long, int)>();
        public static long total;
        static List<long> stack = new List<long>(100);
        public static void Prefix(out bool __state)
        {
            __state = false;
            if (Thread.CurrentThread == main && (__state = Main.EnableLagSpikeProfiling))
                stack.Add(DateTime.UtcNow.Ticks);
        }
        public static void Finalizer(bool __state, MethodBase __originalMethod)
        {
            if (!__state)
                return;
            var t = DateTime.UtcNow.Ticks - stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            for (int i = 0; i < stack.Count; i++)
                stack[i] += t;
            recorded.TryGetValue(__originalMethod, out var o);
            recorded[__originalMethod] = (o.Item1 + t,o.Item2 + 1);
            total += t;
        }
    }

    // Some debugging code. Used for logging various state switches along with a stacktrace
    /*[HarmonyPatch] // Logging some ui actions
    static class Patch_ChangeExclusive
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KAUI), "RemoveExclusive");
            yield return AccessTools.Method(typeof(KAUI), "SetExclusive", new[] { typeof(KAUI), typeof(Texture2D), typeof(Color), typeof(bool) });
        }
        static void Prefix(KAUI iFace)
        {
            var n = new StringBuilder();
            n.Append("UI EVENT: ");
            n.Append(iFace.GetString());
            n.Append("\n");
            Main.GetDetailedString(new StackTrace(1, true), n);
            Debug.Log(n.ToString());
        }
    }

    [HarmonyPatch(typeof(Incubator), "SetIncubatorState")]
    static class Patch_SetIncubatorState
    {
        static void Prefix(Incubator __instance, Incubator.IncubatorStates state)
        {
            var n = new StringBuilder();
            n.Append("SET INCUBATOR STATE: ");
            n.Append(__instance.GetString());
            n.Append("\n -- New: ");
            n.Append(state);
            n.Append("\n -- Old: ");
            n.Append(__instance.pMyState);
            n.Append("\n");
            Main.GetDetailedString(new StackTrace(1, true), n);
            Debug.Log(n.ToString());
        }
    }

    [HarmonyPatch(typeof(UiMultiEggHatching), "GetEmptyIncubatorSlot")]
    static class Patch_GetEmptyIncubatorSlot
    {
        static void Prefix(UiHatchingSlotsMenu ___mHatchingSlotsMenu)
        {
            var n = new StringBuilder();
            n.Append("GetEmptyIncubatorSlot Start: ");
            List<KAWidget> items = ___mHatchingSlotsMenu.GetItems();
            if (items != null && items.Count > 0)
                for (int i = 0; i < items.Count; i++)
                {
                    var widget = (IncubatorWidgetData)items[i].GetUserData();
                    if (widget != null && widget.Incubator)
                    {
                        n.Append("\nIncubator ");
                        n.Append(widget.Incubator);
                        n.Append(" is in state ");
                        n.Append(widget.Incubator.pMyState);
                    }
                }
            Debug.Log(n.ToString());
        }
        static void Postfix(KAWidget __result)
        {
            var n = new StringBuilder();
            n.Append("GetEmptyIncubatorSlot End: ");
            n.Append(__result.GetString());
            Debug.Log(n.ToString());
        }
    }

    [HarmonyPatch]
    static class Patch_CheckArgs
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MissionManager), "AddAction");
            yield return AccessTools.Method(typeof(MissionManager), "DoAction");
            yield return AccessTools.Method(typeof(MissionManager), "ShowActionPopup");
            yield return AccessTools.Method(typeof(MissionManager), "PopupLoadEvent");
            yield return AccessTools.Method(typeof(UiNPCQuestDetails), "OnOfferAction");
        }
        static void Prefix(object[] __args, MethodBase __originalMethod)
        {
            if (Patch_CheckArgsWrapper.log)
                Debug.Log($"{__originalMethod.DeclaringType.FullName}::{__originalMethod.Name}({__args?.Join(x => x?.ToString() ?? "") ?? ""})");
        }
    }

    [HarmonyPatch(typeof(KAUI), "SetState")]
    static class Patch_SetUIState
    {
        static void Prefix(KAUI __instance, KAUIState inState)
        {
            var n = new StringBuilder();
            n.Append("SET WIDGET STATE: ");
            n.Append(__instance.GetString());
            n.Append("\n -- Old: ");
            n.Append(__instance.GetState());
            n.Append("\n -- New: ");
            n.Append(inState);
            n.Append("\n");
            Main.GetDetailedString(new StackTrace(1, true), n);
            Debug.Log(n.ToString());
        }
    }

    [HarmonyPatch(typeof(AvAvatar), "set_pInputEnabled")]
    static class Patch_EnableInput
    {
        static void Prefix(bool value)
        {
            var n = new StringBuilder();
            n.Append("SET INPUT ENABLED:\n -- New: ");
            n.Append(value);
            n.Append("\n -- Old: ");
            n.Append(AvAvatar.pInputEnabled);
            n.Append("\n");
            Main.GetDetailedString(new StackTrace(1, true), n);
            Debug.Log(n.ToString());
        }
    }

    [HarmonyPatch(typeof(AvAvatar), "set_pState")]
    static class Patch_SetState
    {
        static void Prefix(AvAvatarState value)
        {
            var n = new StringBuilder();
            n.Append("SET PLAYER STATE:\n -- New: ");
            n.Append(value);
            n.Append("\n -- Old: ");
            n.Append(AvAvatar.pState);
            n.Append("\n");
            Main.GetDetailedString(new StackTrace(1, true), n);
            Debug.Log(n.ToString());
        }
    }

    [HarmonyPatch(typeof(KAWidget), "SetUserData")]
    static class Patch_SetWidgetData
    {
        static void Prefix(KAWidget __instance, KAWidgetUserData ud)
        {
            if (ud is KAUISelectItemData)
            {
                var n = new StringBuilder();
                n.Append("SET WIDGET DATA:\n -- Widget: ");
                n.Append(__instance.ToString());
                n.Append("\n -- Data: ");
                n.Append(ud);
                n.Append("\n");
                Main.GetDetailedString(new StackTrace(1, true), n);
                Debug.Log(n.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(KAUISelect), "Awake")]
    static class Patch_CreateKAUISelect
    {
        static void Prefix(KAUISelect __instance)
        {
            if (__instance is UiBattleBackpack)
            {
                var n = new StringBuilder();
                n.Append("SET WIDGET DATA:\n -- UI: ");
                n.Append(__instance.ToString());
                n.Append("\n");
                Main.GetDetailedString(new StackTrace(1, true), n);
                Debug.Log(n.ToString());
            }
        }
    }*/
}