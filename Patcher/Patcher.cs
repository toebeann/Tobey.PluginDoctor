using BepInEx.Logging;
using Mono.Cecil;
using System.Collections.Generic;

namespace Tobey.PluginDoctor;
public static class Patcher
{
    // Without the contents of this region, the patcher will not be loaded by BepInEx - do not remove!
    #region BepInEx Patcher Contract
    public static IEnumerable<string> TargetDLLs { get; } = [];
    public static void Patch(AssemblyDefinition _) { }
    #endregion

    internal static Dictionary<string, object> PluginTypeNames_CouldNotBeInstantiated;
    internal static Dictionary<string, object> PluginTypeNames_ThrewInAwake;

    public static void Initialize() => Logger.Listeners.Add(GlobalLogListener.Instance);
}