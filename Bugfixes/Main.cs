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
using System.Globalization;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;






#if DESKTOP
using BepInEx;
using ConfigTweaks;
using System.Diagnostics;
#elif MOBILE
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MobileTools;
using SquadTactics = Il2CppSquadTactics;
using static Il2Cpp.UiWorldEventRewards;
using static MelonLoader.MelonLogger;
using Il2CppInterop.Runtime.InteropTypes;

[assembly: MelonInfo(typeof(Bugfixes.Main), "Client Bugfixes", Bugfixes.Main.VERSION, "Aidanamite")]
[assembly: MelonAdditionalDependencies("MobileTools")]
#endif

namespace Bugfixes
{
#if DESKTOP
    [BepInPlugin("com.aidanamite.Bugfixes", "Client Bugfixes", VERSION)]
    [BepInDependency("com.aidanamite.ConfigTweaks", "1.1.0")]
    public class Main : BaseUnityPlugin
#elif MOBILE
    public class Main : MelonMod
#endif
    {
        public const string VERSION = "1.0.21";
        [ConfigField]
        public static KeyCode ForceInteractable = KeyCode.KeypadMultiply;
        [ConfigField]
        public static bool FixGrowthUI = true;
        [ConfigField]
        public static bool DisplayDragonGender = true;
        [ConfigField]
        public static bool UIFrameThrottling = true;
        [ConfigField]
        public static bool FixEmptyLocaleData = true;
#if DESKTOP
        [ConfigField(Description = "DEV PURPOSES ONLY: This can take a long time to load (the game will be frozen during this time) and may have significant performance impact while active")]
        public static bool EnableLagSpikeProfiling = false;
        [ConfigField]
        public static long LagThreashold = 200000;
        [ConfigField]
        public static double LagDisplayThreashold = 0.5;
#endif

#if DESKTOP
        static BepInEx.Logging.ManualLogSource logger;
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
            logger = Logger;
            new Harmony("com.aidanamite.Bugfixes").PatchAll();
#elif MOBILE
        static MelonLogger.Instance logger;
        public override void OnInitializeMelon()
        {
            logger = LoggerInstance;
            base.OnInitializeMelon();
            HarmonyInstance.PatchAll();
#endif
            LogInfo("Loaded");
        }

#if DESKTOP
        string errMsg;

        public void OnDestroy()
        {
            // Fixes a bug where trying to close the game would simply make it stop responding
            Process.GetCurrentProcess().Kill();
        }
#endif

        public static bool AllowUIUpdate = true;
        int updateStep;
#if DESKTOP
        public void Update()
        {
#elif MOBILE

        public override void OnUpdate()
        {
            base.OnUpdate();
#endif
            updateStep = (updateStep + 1) % ((int)(Patch_Widget.count * 11L / 80000) + 1);
            AllowUIUpdate = !UIFrameThrottling || updateStep == 0;

#if DESKTOP
            if (Input.GetKeyDown(ForceInteractable)) // A workaround for UIs not being interactable when they should've been. Sometimes there's still issues after getting out of the UI
                foreach (var i in FindObjectsOfType<KAUI>())
                    if (i.GetVisibility())
                        i.SetInteractive(true);

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
#endif
        }

#if DESKTOP
        public static void LogInfo(object msg) => logger.LogInfo(msg);
#elif MOBILE
        public static void LogInfo(object msg) => logger.Msg(msg);
#endif
    }

    static class ExtentionMethods
    {
        public static KAWidget GetEmptyIncubatorSlot(this UiHatchingSlotsMenu menu)
        {
            var items = menu.GetItems();
            if (items != null && items.Count > 0)
                for (int i = 0; i < items.Count; i++)
                {
                    var widget = (IncubatorWidgetData)items[i].GetUserData();
                    if (widget != null && widget.Incubator)
                    {
#if DESKTOP
                        Debug.Log($"Incubator {widget.Incubator} is in state {widget.Incubator.pMyState}");
#endif
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

#if DESKTOP
        public static string GetOperandString(this object obj, List<CodeInstruction> code = null) => obj == null ? "" : $"{(obj is Label l ? code == null ? -2 : code.FindIndex(y => y.labels != null && y.labels.Contains(l)) : obj is MemberInfo m && m.DeclaringType != null ? m.DeclaringType.FullName + "::" + m : obj)} [{obj.GetType().FullName}]";

        static FieldInfo _locals = typeof(ILGenerator).GetField("locals", ~BindingFlags.Default);
        public static LocalBuilder[] GetLocals(this ILGenerator generator) => (LocalBuilder[])_locals.GetValue(generator);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is<T>(this object obj) => obj is T;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is<T>(this object obj,out T nObj)
        {
            if (obj is T n)
            {
                nObj = n;
                return true;
            }
            nObj = default;
            return false;
        }

        public static IEnumerable<T> ToEnumerable<T>(this IEnumerable<T> l) => l;
#elif MOBILE
        public static (LocaleString, Gender) ToTarget(this RaisedPetData data) => data == null ? default : (SanctuaryData.FindSanctuaryPetTypeInfo(data.PetTypeID)._NameText, data.Gender);
        public static (LocaleString, Gender) ToTarget(this HeroPetData data) => data == null ? default : (SanctuaryData.FindSanctuaryPetTypeInfo(data._TypeID)._NameText, data._Gender);

        public static Il2CppSystem.Collections.Generic.IEnumerable<T> ToEnumerable<T>(this Il2CppSystem.Collections.Generic.List<T> l) => l.Cast<Il2CppSystem.Collections.Generic.IEnumerable<T>>();
#endif
    }

#if DESKTOP
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
#endif

    // Fixes a bug where certain objects had a null/destroyed path object which would cause the entire pathing to fail and the object to not move at all
    [HarmonyPatch] 
    static class Patch_SplineFix
    {
        static MethodBase TargetMethod() => typeof(NPCSplineMove).GetMethods(~BindingFlags.Default).First(x => x.Name.Contains("StartMove") && x.Name != "StartMove");
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

    // Fixes a bug caused by the clan color button having been renamed in the assetbundle causing an error during UI initialization and softlocking the game
    [HarmonyPatch]
    static class Patch_ItemCustomization
    {
        static UiAvatarItemCustomization current;

        [HarmonyPatch(typeof(UiAvatarItemCustomization), "Start")]
        static void Prefix(UiAvatarItemCustomization __instance, ref UiAvatarItemCustomization __state)
        {
            __state = current;
            current = __instance;
        }
        [HarmonyPatch(typeof(UiAvatarItemCustomization), "Start")]
        static void Postfix(UiAvatarItemCustomization __state) => current = __state;

        [HarmonyPatch(typeof(KAUI), "FindItem", typeof(string), typeof(bool))]
        static bool Prefix(KAUI __instance, string inWidgetName, ref KAWidget __result)
        {
            if (__instance == current)
            {
                if ((inWidgetName == "SyncColorBtn" && (__result = __instance.FindItem("CrestBGColorBtn", true)))
                    || (inWidgetName == "SyncClanColorBtn" && (__result = __instance.FindItem("CrestFGColorBtn"))))
                    return false;
            }
            return true;
        }
    }

    // Fixes a bug where the server would double send a mission complete message that causes an error that softlocks the game
    [HarmonyPatch(typeof(MissionManager), "MissionCompleteCallback")]
    static class Patch_MissionComplete
    {
#if DESKTOP
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].operand is MethodInfo m && m.DeclaringType.IsConstructedGenericType && m.Name == "Add" && typeof(Dictionary<,>) == m.DeclaringType.GetGenericTypeDefinition())
                    code[i].operand = AccessTools.Method(m.DeclaringType, "set_Item");
            return code;
        }
#elif MOBILE
        static Exception Finalizer(Exception __exception) => __exception is ArgumentException ? null : __exception;
#endif
    }

    // An attempt to fix certain music being treated like general sound effects
    [HarmonyPatch]
    static class Patch_SoundGroup
    {
        //static HashSet<(string, PoolGroup)> found = new HashSet<(string, PoolGroup)>();
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SnChannel), "ApplySettings");
            yield return AccessTools.Method(typeof(SnChannel), "AddChannel", new[] { typeof(SnChannel) });
            yield return AccessTools.Method(typeof(SnChannel), "SetVolumeForPoolGroup");
            yield return AccessTools.Method(typeof(SnChannel), "TurnOffPools");
        }
#if DESKTOP
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f && f.Name == "_Group")
                    code.Insert(i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_SoundGroup), nameof(CheckGroup))));
            return code;
        }
#elif MOBILE
        static void Prefix()
        {
            foreach (var pool in SnChannel.mTurnedOffPools)
                CheckGroup(pool);
        }
#endif
        static PoolInfo CheckGroup(PoolInfo instance)
        {
            if (instance._Name == "AmbSFX_Pool")
                instance._Group = PoolGroup.MUSIC;
            //if (found.Add((instance._Name, instance._Group)))
            //Main.LogSource.LogInfo($"Pool info {instance._Group} \"{instance._Name}\"");
            return instance;
        }
    }

    // Fixes a bug where putting an egg into the hatchery *after* completing a tutorial in the same area would cause a UI bug
    [HarmonyPatch(typeof(Incubator), "CheckEggSelected")] 
    static class Patch_CheckEggSelected
    {
#if DESKTOP
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
#elif MOBILE
        static void Prefix()
        {
            if ((object)InteractiveTutManager._CurrentActiveTutorialObject != null && InteractiveTutManager._CurrentActiveTutorialObject == null)
                InteractiveTutManager._CurrentActiveTutorialObject = null;
        }
#endif
    }

    // Fixes a bug with some mission reward popups softlocking the game
    [HarmonyPatch(typeof(RewardWidget), "AddRewardItem")] 
    static class Patch_AddRewards
    {
#if DESKTOP
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code[code.FindIndex(x => x.opcode == OpCodes.Ldelem)] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_AddRewards), nameof(GetItemSafe)));
            return code;
        }
        static Vector2 GetItemSafe(Vector2[] array, int index) => array.Length > index ? array[index] : default;
#elif MOBILE
        static void Prefix(RewardWidget __instance, RewardWidget.RewardPositionsData inRewardPositionData)
        {
            if (__instance.mAddRewardCallIndex >= inRewardPositionData._Positions.Length)
            {
                var p = inRewardPositionData._Positions.Cast<Il2CppArrayBase<Vector2>>();
                Il2CppSystem.Array.Resize(ref p, __instance.mAddRewardCallIndex + 1);
                inRewardPositionData._Positions = p.Cast<Il2CppStructArray<Vector2>>();
            }
        }
#endif
    }

    // Fixes a bug where the dragon age up pop up could open on top of another ui
    [HarmonyPatch(typeof(ObProximityHatch), "Update")] 
    static class Patch_GrowUI
    {
#if DESKTOP
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
        static SanctuaryPet CheckOpenAgeUpUI(SanctuaryPet original, ObProximityHatch instance) => (!Main.FixGrowthUI || (UINotBlocked && memory.GetOrCreateValue(instance).lifetime > 5)) ? original : null;
        static ConditionalWeakTable<ObProximityHatch, Memory> memory = new ConditionalWeakTable<ObProximityHatch, Memory>();
        static void CheckTime(ObProximityHatch instance)
        {
            if (UINotBlocked)
                memory.GetOrCreateValue(instance).lifetime += Time.deltaTime;
        }
        class Memory
        {
            public float lifetime;
        }
#elif MOBILE
        static bool Prefix(ObProximityHatch __instance)
        {
            if (UINotBlocked)
            {
                var memory = __instance.GetComponent<Memory>();
                if (!memory)
                    memory = __instance.gameObject.AddComponent<Memory>();
                memory.lifetime += Time.deltaTime;
                return !Main.FixGrowthUI || memory.lifetime > 5;
            }
            return true;
        }
        [RegisterTypeInIl2Cpp]
        public class Memory : MonoBehaviour
        {
            public Memory(IntPtr ptr) : base(ptr) {}
            public float lifetime;
        }
#endif

        static bool UINotBlocked => AvAvatar.pInputEnabled && AvAvatar.pState != AvAvatarState.PAUSED && AvAvatar.pState != AvAvatarState.NONE;
    }

    // Fixes a bug where the npc quest dialog will sometimes never become interactable. While not ideal, it currently works by stopping it being disabled in the first place
    [HarmonyPatch(typeof(UiNPCQuestDetails), "SetupDetailsUi")]
    static class Patch_SetupQuestUI
    {
#if DESKTOP
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code[code.FindLastIndex(x => x.operand is MethodInfo m && m.Name == "SetState") - 1].opcode = OpCodes.Ldc_I4_0;
            return code;
        }
#elif MOBILE
        static void Postfix(UiNPCQuestDetails __instance) => __instance.SetState(KAUIState.INTERACTIVE);
#endif
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

    // Fixes an issue where 
    [HarmonyPatch(typeof(RaisedPetData), "ServiceEventHandler")]
    static class Patch_AppendPetData
    {
#if DESKTOP
        static void Prefix(WsServiceType inType, WsServiceEvent inEvent, float inProgress, ref object inObject, object inUserData)
#else
        static void Prefix(WsServiceType inType, WsServiceEvent inEvent, float inProgress, ref Il2CppSystem.Object inObject, Il2CppSystem.Object inUserData)
#endif
        {
            if (inEvent == WsServiceEvent.COMPLETE && inUserData.Is<RaisedPetGetData>()
#if DESKTOP
                && inObject.Is<RaisedPetData[]>(out var parray))
#else
                && inObject.Is<Il2CppReferenceArray<RaisedPetData>>(out var parray))
#endif
            {
                foreach (var p in parray)
                    if (!IsntFish(p))
                    {
                        if (p.IsSelected)
                        {
                            p.IsSelected = false;
                            Release(p);
                        }
                        Release(p);
                    }
#if DESKTOP
                inObject = parray.Where(IsntFish).ToArray();
#else
                inObject = new Il2CppReferenceArray<RaisedPetData>(parray.Where(IsntFish).ToArray()).Cast<Il2CppSystem.Object>();
#endif
            }
        }

        static bool IsntFish(RaisedPetData data) => data.PetTypeID != 2;

        static void Release(RaisedPetData petData)
        {
#if DESKTOP
            petData.ReleasePet(null);
#else
            ServiceRequest serviceRequest = new ServiceRequest();
            serviceRequest._Type = WsServiceType.SET_RAISED_PET_INACTIVE;
            WsWebService.AddCommon(serviceRequest);
            serviceRequest.AddParam("raisedPetID", petData.RaisedPetID);
            string text = WsWebService.Ticks.ToString();
            serviceRequest.AddParam("ticks", text);
            serviceRequest.AddParam("signature", WsMD5Hash.GetMd5Hash(text + WsWebService.mSecret + WsWebService.mUserToken + petData.RaisedPetID.ToString()));
            serviceRequest._URL = WsWebService.mContentURL + "SetRaisedPetInactive";
            ServiceCall<bool> serviceCall = ServiceCall<bool>.Create(serviceRequest, ServiceCallType.NONE);
            if (serviceCall == null)
                return;
            serviceCall.DoSet();
#endif
        }
    }

    // Fixes a bug with the age up prompt where the player's control would not be restored when closing the ui
#if DESKTOP
    [HarmonyPatch(typeof(DragonAgeUpConfig), "ShowAgeUpUI", typeof(DragonAgeUpConfig.OnDragonAgeUpDone), typeof(RaisedPetStage), typeof(RaisedPetData), typeof(RaisedPetStage[]), typeof(bool), typeof(bool), typeof(GameObject), typeof(string))]
    static class Patch_ShowAgeUpUI_SimpleToComplex
    {
        static bool Prefix(DragonAgeUpConfig.OnDragonAgeUpDone inOnDoneCallback, RaisedPetStage fromStage, RaisedPetData inData, RaisedPetStage[] requiredStages, bool ageUpDone, bool isUnmountableAllowed, GameObject messageObj, string assetName)
        {
            if (inOnDoneCallback != null)
            {
                DragonAgeUpConfig.ShowAgeUpUI(inOnDoneCallback.Invoke, inOnDoneCallback, inOnDoneCallback.Invoke, fromStage, inData, requiredStages, ageUpDone, isUnmountableAllowed, messageObj, assetName);
                return false;
            }
            return true;
        }
    }
#else
    [HarmonyPatch(typeof(DragonAgeUpConfig))]
    static class Patch_AgeUpUI
    {
        static int state = 0;

        [HarmonyPatch("CleanupCallbacks")]
        [HarmonyPrefix]
        static void CleanupCallbacks()
        {
            if (state == 1 || state == 2)
                DragonAgeUpConfig.OnUiDragonAgeUpClose();
        }


        [HarmonyPatch("OnUiDragonAgeUpCancel")]
        [HarmonyPrefix]
        static void OnUiDragonAgeUpCancel_Pre(out int __state)
        {
            __state = state;
            state = 1;
        }
        [HarmonyPatch("OnUiDragonAgeUpCancel")]
        [HarmonyFinalizer]
        static void OnUiDragonAgeUpCancel_Post(int __state) => state = __state;


        [HarmonyPatch("OnUiDragonAgeUpBuy")]
        [HarmonyPrefix]
        static void OnUiDragonAgeUpBuy_Pre(out int __state)
        {
            __state = state;
            state = 2;
        }
        [HarmonyPatch("OnUiDragonAgeUpBuy")]
        [HarmonyFinalizer]
        static void OnUiDragonAgeUpBuy_Post(int __state) => state = __state;


        [HarmonyPatch("OnUiDragonAgeUpClose")]
        [HarmonyPrefix]
        static void OnUiDragonAgeUpClose_Pre(out int __state)
        {
            __state = state;
            state = 3;
        }
        [HarmonyPatch("OnUiDragonAgeUpClose")]
        [HarmonyFinalizer]
        static void OnUiDragonAgeUpClose_Post(int __state) => state = __state;
    }
#endif

#if DESKTOP
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
#elif MOBILE
    // Adds the dragon gender display for several non-static methods
    [HarmonyPatch]
    static class Patch_DisplayDragonGender_NonStatic
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KAUISelectDragonMenu), "SelectItem");
            yield return AccessTools.Method(typeof(KAUISelectDragonMenu), "GetGlowFailFormatText");
            yield return AccessTools.Method(typeof(UiChooseADragon), "SetPlayerDragonData");
            yield return AccessTools.Method(typeof(UiDragonCustomization), "RefreshUI");
            yield return AccessTools.Method(typeof(UiDragonCustomization), "ShowDragonStats");
            yield return AccessTools.Method(typeof(UiDragonsAgeUpMenuItem), "SetupWidget");
            yield return AccessTools.Method(typeof(UiDragonsInfoCardItem), "RefreshUI");
            yield return AccessTools.Method(typeof(UiDragonsListCardMenu), "LoadDragonsList");
            yield return AccessTools.Method(typeof(UiMessageInfoUserData), "ReplaceTagWithPetData");
            yield return AccessTools.Method(typeof(UiMOBASelectDragon), "SetPlayerDragonData");
            yield return AccessTools.Method(typeof(UiSelectHeroDragons), "SetPlayerDragonData");
            yield return AccessTools.Method(typeof(UiStableQuestDragonSelect), "RefreshUIWidgets");
            yield break;
        }
        static void Prefix(Il2CppSystem.Object __instance)
        {
            if (Main.DisplayDragonGender)
            {
                if (__instance.Is<KAUISelectDragonMenu>(out var sdm))
                    Patch_GetLocalizedString.Target = sdm.mUiSelectDragon.mPetData.ToTarget();
                else if (__instance.Is<UiChooseADragon>() || __instance.Is<UiMOBASelectDragon>() || __instance.Is<UiSelectHeroDragons>())
                    Patch_GetLocalizedString.Target = SanctuaryManager.pCurPetInstance.pData.ToTarget();
                else if (__instance.Is<UiDragonCustomization>(out var dc))
                    Patch_GetLocalizedString.Target = dc.mPetData.ToTarget();
                else if (__instance.Is<UiDragonsAgeUpMenuItem>(out var aumi) && aumi.GetUserData().Is<AgeUpUserData>(out var agedata))
                    Patch_GetLocalizedString.Target = agedata.pData.ToTarget();
                else if (__instance.Is<UiDragonsInfoCardItem>(out var dici))
                    Patch_GetLocalizedString.Target = dici.mSelectedPetData.ToTarget();
                else if (__instance.Is<UiDragonsListCardMenu>(out var dlcm))
                {
                    Patch_GetLocalizedString.ClearTargets();
                    foreach (var array in RaisedPetData.pActivePets.Values)
                        foreach (var data in array)
                            if (
                                    (SanctuaryManager.pCurPetInstance == null
                                    || SanctuaryManager.pCurPetInstance.pData == null
                                    || data.RaisedPetID != SanctuaryManager.pCurPetInstance.pData.RaisedPetID)
                                && data.pStage >= RaisedPetStage.BABY
                                && data.IsPetCustomized())
                            {
                                if (dlcm._UiDragonsListCard.pCurrentMode == UiDragonsListCard.Mode.NestedDragons || dlcm._UiDragonsListCard.pCurrentMode == UiDragonsListCard.Mode.ForceDragonSelection)
                                {
                                    if (StableData.GetByPetID(data.RaisedPetID) == null)
                                        continue;
                                }
                                else if (dlcm._UiDragonsListCard.pCurrentMode == UiDragonsListCard.Mode.CurrentStableDragons)
                                {
                                    if (StableData.GetByPetID(data.RaisedPetID)?.ID != StableManager.pCurrentStableID)
                                        continue;
                                }
                                Patch_GetLocalizedString.AddTarget(data.ToTarget());
                            }
                }
                else if (__instance.Is<UiMessageInfoUserData>(out var miud))
                {
                    var reward = string.IsNullOrEmpty(miud.mMessageInfo.Data) ? null : (UtUtilities.DeserializeFromXml(miud.mMessageInfo.Data, typeof(Il2Cpp.RewardData).ToIl2Cpp()).Is<Il2Cpp.RewardData>(out var r) ? r : null);
                    var entity = reward != null && !string.IsNullOrEmpty(reward.EntityID) ? RaisedPetData.GetByEntityID(new NullableStruct<Il2CppSystem.Guid>(new Il2CppSystem.Guid(reward.EntityID))) : null;
                    if (entity != null)
                        Patch_GetLocalizedString.Target = entity.ToTarget();
                }
                else if (__instance.Is<UiStableQuestDragonSelect>(out var sqds))
                    Patch_GetLocalizedString.Target = sqds.mCurrentPetUserData.pData.ToTarget();
            }
        }
        static void Finalizer()
        {
            Patch_GetLocalizedString.ClearTargets();
        }
    }
    [HarmonyPatch]
    static class Patch_DisplayDragonGender_ArgumentsPrefixes
    {
        [HarmonyPatch(typeof(Il2CppJSGames.UI.Util.UIUtil), "ReplaceTagWithPetData")]
        [HarmonyPrefix]
        static void ReplaceTagWithPetData(Il2Cpp.RewardData inRewardData)
        {
            if (Main.DisplayDragonGender)
            {
                var entity = inRewardData != null && !string.IsNullOrEmpty(inRewardData.EntityID) ? RaisedPetData.GetByEntityID(new NullableStruct<Il2CppSystem.Guid>(new Il2CppSystem.Guid(inRewardData.EntityID))) : null;
                if (entity != null)
                    Patch_GetLocalizedString.Target = entity.ToTarget();
            }
        }
        [HarmonyPatch(typeof(UiDragonsListCard), "SetSelectedDragonItem")]
        [HarmonyPrefix]
        static void SetSelectedDragonItem(RaisedPetData pData)
        {
            if (Main.DisplayDragonGender)
                Patch_GetLocalizedString.Target = pData.ToTarget();
        }
        [HarmonyPatch(typeof(UiSelectHeroDragons), "AddDragonDetails")]
        [HarmonyPrefix]
        static void AddDragonDetails(HeroPetData hpData)
        {
            if (Main.DisplayDragonGender)
                Patch_GetLocalizedString.Target = hpData.ToTarget();
        }
        [HarmonyPatch(typeof(UiStableQuestCompleteMenu), "PopulateItems")]
        [HarmonyPrefix]
        static void PopulateItems(TimedMissionSlotData slotData)
        {
            if (Main.DisplayDragonGender)
            {
                Patch_GetLocalizedString.ClearTargets();
                for (int i = 0; i < slotData.PetIDs.Count; i++)
                {
                    RaisedPetData byID = RaisedPetData.GetByID(slotData.PetIDs[i]);
                    if (byID != null)
                        Patch_GetLocalizedString.AddTarget(byID.ToTarget());
                }
            }
        }
        [HarmonyPatch(typeof(WsUserMessage), "ShowSystemMessage")]
        [HarmonyPrefix]
        static void ShowSystemMessage(MessageInfo messageInfo)
        {
            if (Main.DisplayDragonGender)
            {
                var reward = string.IsNullOrEmpty(messageInfo.Data) ? null : (UtUtilities.DeserializeFromXml(messageInfo.Data, typeof(Il2Cpp.RewardData).ToIl2Cpp()) as Il2Cpp.RewardData);
                var entity = reward != null && !string.IsNullOrEmpty(reward.EntityID) ? RaisedPetData.GetByEntityID(new NullableStruct<Il2CppSystem.Guid>(new Il2CppSystem.Guid(reward.EntityID))) : null;
                if (entity != null)
                    Patch_GetLocalizedString.Target = entity.ToTarget();
            }
        }
    }
    [HarmonyPatch]
    static class Patch_DisplayDragonGender_ArgumentsFinalizers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Il2CppJSGames.UI.Util.UIUtil), "ReplaceTagWithPetData");
            yield return AccessTools.Method(typeof(UiDragonsListCard), "SetSelectedDragonItem");
            yield return AccessTools.Method(typeof(UiSelectHeroDragons), "AddDragonDetails");
            yield return AccessTools.Method(typeof(UiStableQuestCompleteMenu), "PopulateItems");
            yield return AccessTools.Method(typeof(WsUserMessage), "ShowSystemMessage");
            yield break;
        }
        static void Finalizer()
        {
            Patch_GetLocalizedString.ClearTargets();
        }
    }

    // Prefixes the string with the gender
    [HarmonyPatch(typeof(LocaleString), "GetLocalizedString")]
    static class Patch_GetLocalizedString
    {
        static int Pos;
        static List<(LocaleString Target, Gender Gender)> Targets = new();
        public static (LocaleString Target, Gender Gender) Target
        {
            set
            {
                ClearTargets();
                AddTarget(value);
            }
        }
        public static void ClearTargets()
        {
            Targets.Clear();
            Pos = 0;
        }
        public static void AddTarget((LocaleString Target, Gender Gender) Target)
        {
            if (Target.Target != null)
                Targets.Add(Target);
        }
        static HashSet<string> modified = new HashSet<string>();
        static void Postfix(LocaleString __instance, ref string __result)
        {
            if (Targets.Count != 0 && __instance._ID == Targets[Pos].Target._ID && __instance._Text == Targets[Pos].Target._Text && !modified.Contains(__result))
            {
                __result = Targets[Pos].Gender + " " + __result;
                modified.Add(__result);
                Pos = (Pos + 1) % Targets.Count;
            }

        }
    }
#endif

    // Makes the UI update when changing the dragon's gender so that the gender display in the hatching UI doesn't just say "Male" the whole time
    [HarmonyPatch(typeof(UiDragonCustomization),"OnClick")]
    static class Patch_UpdateCustomization
    {
        static void Postfix(UiDragonCustomization __instance, KAWidget inItem)
        {
            if (Main.DisplayDragonGender && (inItem == __instance.mToggleBtnMale || inItem == __instance.mToggleBtnFemale))
            {
                __instance.pPetData.Gender = __instance.mDragonMale ? Gender.Male : Gender.Female;
                __instance.mUiRefresh = true;
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
            if (__instance.Is < UiDragonCustomization>(out var ui))
                ui.mDragonMale = ui.pPetData.Gender == Gender.Male;
        }
    }

    // Fixes a bug where the default enabled gender button was not the same as the current dragon's gender
    [HarmonyPatch(typeof(UiDragonCustomization), "Initialize")]
    static class Patch_InitDragonCustomization
    {
        static void Postfix(UiDragonCustomization __instance)
        {
            if (__instance.mToggleBtnMale)
            {
                __instance.mToggleBtnMale.SetChecked(__instance.mDragonMale);
                __instance.mToggleBtnMale._StartChecked = __instance.mDragonMale;
            }
            if (__instance.mToggleBtnFemale)
            {
                __instance.mToggleBtnFemale.SetChecked(!__instance.mDragonMale);
                __instance.mToggleBtnFemale._StartChecked = !__instance.mDragonMale;
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
                __instance.pGamePlayTime = __instance.pGamePlayTime + Time.deltaTime;
            return false;
        }
    }

    // Fixes some FPS issues in Dragon Tactics caused by use of GameObject.Find
#if DESKTOP
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
#elif MOBILE
    [HarmonyPatch(typeof(GameObject), "Find")]
    static class Patch_FindGameObject
    {
        public static Dictionary<string,GameObject> found = new();
        static bool Prefix(string name, ref GameObject __result)
        {
            if (found.TryGetValue(name, out var go) && go)
            {
                __result = go;
                return false;
            }
            return true;
        }
        static void Postfix(string name, GameObject __result)
        {
            if (__result)
                found[name] = __result;
        }
    }
#endif

    // Fixes some FPS issues in Dragon Tactics caused the by the game loading the battle backpack early for some reason
    [HarmonyPatch(typeof(KAUISelectMenu), "FinishMenuItems")]
    static class Patch_PreventItemMenuLoad
    {
        public static bool BypassFix = false;
        static bool Prefix() => !SquadTactics.GameManager.pInstance || BypassFix;
    }

    [HarmonyPatch(typeof(SquadTactics.UiEndDB), "SetRewards")]
    static class Patch_DisplayDTEndResult
    {
        static void Postfix(SquadTactics.UiEndDB __instance)
        {
            Patch_PreventItemMenuLoad.BypassFix = true;
            __instance.mBattleBackPack.pKAUiSelectMenu.FinishMenuItems(false);
            Patch_PreventItemMenuLoad.BypassFix = false;
        }
    }

    // Fix a softlock issue with scout ship battle events where a second event could start during the rewards screen causing the score list to clear and break the UI
    [HarmonyPatch(typeof(WorldEventMMOClient), "ParseRoomVariables")]
    static class Patch_ParseScoutAttack
    {
#if DESKTOP
        static void Prefix(ref List<KnowledgeAdventure.Multiplayer.Model.MMORoomVariable> roomVars, List<object> changedKeys)
#else
        static void Prefix(ref Il2CppSystem.Collections.Generic.List<Il2CppKnowledgeAdventure.Multiplayer.Model.MMORoomVariable> roomVars, Il2CppSystem.Collections.Generic.List<Il2CppSystem.Object> changedKeys)
#endif
        {
            if (roomVars != null)
            {
                for (int i = 0; i < roomVars.Count; i++)
                    if ("WE_ScoutAttack".Equals(roomVars[i].Name))
                    {
                        if (!changedKeys.Contains(roomVars[i].Name))
                        {
                            roomVars = new(roomVars.ToEnumerable());
                            roomVars.RemoveAt(i);
                        }
                        break;
                    }
            }
        }
    }

    // Fix a softlock caused by the client thinking they participated in the event but they aren't on the score board
    [HarmonyPatch(typeof(WorldEventScoutAttack), "PopulateScore")]
    static class Patch_PopulateScoutAttackScores
    {
#if DESKTOP
        static void Prefix(ref string[] playersData)
#else
        static void Prefix(ref Il2CppStringArray playersData)
#endif
        {
            var look = AvatarData.pInstance.DisplayName + "/";
            foreach (var s in playersData)
                if (s.StartsWith(look))
                    return;
#if DESKTOP
            Array.Resize(ref playersData, playersData.Length + 1);
#else
            var p = playersData.Cast<Il2CppArrayBase<Il2CppSystem.String>>();
            Il2CppSystem.Array.Resize(ref p, p.Length + 1);
            playersData = p.Cast<Il2CppStringArray>();
#endif
            playersData[playersData.Length - 1] = look + "0";
        }
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

    // Completely replaces all the methods in the HexUtil class because the vanilla code is terribly ineffecient and likely to error
    [HarmonyPatch(typeof(HexUtil))]
    static class Patch_HexUtil
    {
        [HarmonyPatch("HexToInt")]
        [HarmonyPrefix]
        static bool HexToInt(string value,out int __result)
        {
            int.TryParse(value, NumberStyles.HexNumber, NumberFormatInfo.CurrentInfo, out __result);
            return false;
        }

        [HarmonyPatch("IntToHex")]
        [HarmonyPrefix]
        static bool IntToHex(int value, out string __result)
        {
            __result = Math.Max(Math.Min(value, 255),0).ToString("X2");
            return false;
        }

        [HarmonyPatch("ColorStringToHex")]
        [HarmonyPrefix]
        static bool ColorStringToHex(string value, out string __result)
        {
            var array = value.Split(new[] { ',' }, StringSplitOptions.None);
            if (array.Length == 3 || array.Length == 4)
            {
                int.TryParse(array[0].Trim(), out var r);
                int.TryParse(array[1].Trim(), out var g);
                int.TryParse(array[2].Trim(), out var b);
                var aStr = "FF";
                if (array.Length == 4)
                {
                    int.TryParse(array[3].Trim(), out var a);
                    aStr = HexUtil.IntToHex(a);
                }
                __result = HexUtil.IntToHex(r) + HexUtil.IntToHex(g) + HexUtil.IntToHex(b) + aStr;
            }
            else
                __result = "";
            return false;
        }

        [HarmonyPatch("FloatToHex")]
        [HarmonyPrefix]
        static bool FloatToHex(float value, out string __result)
        {
            __result = HexUtil.IntToHex((int)Math.Round(Math.Max(int.MinValue, Math.Min(value,(double)int.MaxValue))));
            return false;
        }

        [HarmonyPatch("HexToColor")]
        [HarmonyPrefix]
        static bool HexToColor(string value, out Color color, out bool __result)
        {
            if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, NumberFormatInfo.CurrentInfo, out var num))
            {
                color = Color.white;
                __result = false;
                return false;
            }
            color = new Color((num >> 24 & 255) / 255f, (num >> 16 & 255) / 255f, (num >> 8 & 255) / 255f, (num & 255) / 255f);
            __result = true;
            return false;
        }

        [HarmonyPatch("HexToRGB")]
        [HarmonyPrefix]
        static bool HexToRGB(string value, out Color __result)
        {
            if (value.Length < 6 || !uint.TryParse(value, NumberStyles.HexNumber, NumberFormatInfo.CurrentInfo, out var num))
            {
                __result = Color.white;
                return false;
            }
            var offset = (value.Length - 6) * 4;
            __result = new Color((num >> (16 + offset) & 255) / 255f, (num >> (8 + offset) & 255) / 255f, (num >> offset & 255) / 255f);
            return false;
        }
    }

    [HarmonyPatch(typeof(StringTable), "GetStringData")]
    static class Patch_FixLocale
    {
        static void Postfix(int id, string defaultTxt, ref string __result)
        {
            if (id != 0 && StringTable.pInstance != null && Main.FixEmptyLocaleData)
                if (string.IsNullOrEmpty(__result))
                    __result = string.IsNullOrEmpty(defaultTxt) ? $"[MISSING LANG TEXT: {id}]" : defaultTxt;
        }
    }

#if DESKTOP
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
#endif

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