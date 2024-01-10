﻿using BepInEx;
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
    [BepInPlugin("com.aidanamite.Bugfixes", "Client Bugfixes", "1.0.0")]
    [BepInDependency("com.aidanamite.ConfigTweaks")]
    public class Main : BaseUnityPlugin
    {
        [ConfigField]
        public static KeyCode ForceInteractable = KeyCode.KeypadMultiply;
        [ConfigField]
        public static bool FixGrowthUI = true;
        [ConfigField]
        public static bool DisplayDragonGender = true;

        public static BepInEx.Logging.ManualLogSource LogSource;
        public void Awake()
        {
            LogSource = Logger;
            new Harmony("com.aidanamite.Bugfixes").PatchAll();
            Logger.LogInfo("Loaded");
        }

        public void OnDestroy()
        {
            // Fixes a bug where trying to close the game would simply make it stop responding
            Process.GetCurrentProcess().Kill();
        }

        public void Update()
        {
            if (Input.GetKeyDown(ForceInteractable)) // A workaround for UIs not being interactable when they should've been. Sometimes there's still issues after getting out of the UI
                foreach (var i in FindObjectsOfType<KAUI>())
                    if (i.GetVisibility())
                        i.SetInteractive(true);
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
            foreach (var t in typeof(DragonClass).Assembly.GetTypes())
                if (!t.IsGenericTypeDefinition && t != typeof(UiStoreDragonStat) && t != typeof(UiTitanInfo))
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

    [HarmonyPatch(typeof(KAUISelectDragon), "set_pPetData")]
    static class Patch_SetSelectedPet
    {
        static void Postfix(KAUISelectDragon __instance)
        {
            if (__instance is UiDragonCustomization ui)
                ui.IsMale(ui.pPetData.Gender == Gender.Male);
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
    }*/
}