using BepInEx;
using BepInEx.Bootstrap;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Tobey.PluginDoctor;
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static readonly Version EmptyVersion = new();

    private IEnumerable<Assembly> staticAssemblies;
    private IEnumerable<Assembly> StaticAssemblies =>
        staticAssemblies ??= AppDomain
            .CurrentDomain
            .GetAssemblies()
            .Where(assembly =>
                assembly.ManifestModule is not ModuleBuilder moduleBuilder ||
                !moduleBuilder.IsTransient());

    private HashSet<BepInEx.PluginInfo> pluginsWithNoKnownIssues;

    private Dictionary<BepInEx.PluginInfo, IEnumerable<BepInProcess>> skippedPlugins_ProcessMismatch;
    private Dictionary<BepInEx.PluginInfo, IEnumerable<BepInEx.PluginInfo>> skippedPlugins_IncompatibilitiesPresent;
    private Dictionary<BepInEx.PluginInfo, IEnumerable<BepInEx.PluginInfo>> skippedPlugins_SkippedDependencies;
    private Dictionary<BepInEx.PluginInfo, IEnumerable<BepInDependency>> skippedPlugins_MissingDependencies;
    private HashSet<BepInEx.PluginInfo> skippedPlugins_ReasonsUnknown;

    private Dictionary<BepInEx.PluginInfo, object> failedPlugins_CouldNotBeInstantiated;
    private Dictionary<BepInEx.PluginInfo, IEnumerable<BepInEx.PluginInfo>> failedPlugins_MissingBepInDependencyAttributes;
    private Dictionary<BepInEx.PluginInfo, object> failedPlugins_ThrewInAwake;

    private Dictionary<BepInEx.PluginInfo, IEnumerable<AssemblyName>> suspiciousPlugins_MissingReferences;
    private Dictionary<BepInEx.PluginInfo, Version> suspiciousPlugins_TargetsWrongBepInExVersion;

    public IEnumerable<BepInEx.PluginInfo> AsymptomaticPlugins =>
        Enumerable.Empty<BepInEx.PluginInfo>()
            .Concat(pluginsWithNoKnownIssues ?? Enumerable.Empty<BepInEx.PluginInfo>());

    public IEnumerable<BepInEx.PluginInfo> SymptomaticPlugins =>
        Enumerable.Empty<BepInEx.PluginInfo>()
            .Concat(skippedPlugins_ProcessMismatch?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_IncompatibilitiesPresent?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_SkippedDependencies?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_MissingDependencies?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_ReasonsUnknown ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(failedPlugins_CouldNotBeInstantiated?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(failedPlugins_MissingBepInDependencyAttributes?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(failedPlugins_ThrewInAwake?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(suspiciousPlugins_MissingReferences?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(suspiciousPlugins_TargetsWrongBepInExVersion?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Distinct();

    public IEnumerable<BepInEx.PluginInfo> SkippedPlugins =>
        Enumerable.Empty<BepInEx.PluginInfo>()
            .Concat(skippedPlugins_ProcessMismatch?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_IncompatibilitiesPresent?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_SkippedDependencies?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_MissingDependencies?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(skippedPlugins_ReasonsUnknown ?? Enumerable.Empty<BepInEx.PluginInfo>());

    private Dictionary<BepInEx.PluginInfo, IEnumerable<AssemblyName>> FailedPlugins_MissingReferences =>
        (suspiciousPlugins_MissingReferences ?? Enumerable.Empty<KeyValuePair<BepInEx.PluginInfo, IEnumerable<AssemblyName>>>())
            .Where(entry => Patcher.PluginTypeNames_CouldNotBeInstantiated?.ContainsKey(entry.Key.TypeName) ?? false)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    public IEnumerable<BepInEx.PluginInfo> FailedPlugins =>
        Enumerable.Empty<BepInEx.PluginInfo>()
            .Concat(FailedPlugins_MissingReferences.Keys)
            .Concat(failedPlugins_CouldNotBeInstantiated?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(failedPlugins_MissingBepInDependencyAttributes?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>())
            .Concat(failedPlugins_ThrewInAwake?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>());

    private Dictionary<BepInEx.PluginInfo, IEnumerable<AssemblyName>> SuspiciousPlugins_MissingReferences =>
        (suspiciousPlugins_MissingReferences ?? Enumerable.Empty<KeyValuePair<BepInEx.PluginInfo, IEnumerable<AssemblyName>>>())
            .Except(FailedPlugins_MissingReferences)
            .Where(entry => (!failedPlugins_MissingBepInDependencyAttributes?.ContainsKey(entry.Key)) ?? true)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    public IEnumerable<BepInEx.PluginInfo> SuspiciousPlugins =>
        Enumerable.Empty<BepInEx.PluginInfo>()
            .Concat(SuspiciousPlugins_MissingReferences.Keys)
            .Concat(suspiciousPlugins_TargetsWrongBepInExVersion?.Keys ?? Enumerable.Empty<BepInEx.PluginInfo>());

    private Version bepinexVersion;
    private Version BepInExVersion => bepinexVersion ??= Assembly.GetAssembly(typeof(Chainloader)).GetName().Version switch
    {
        Version version => new(version.Major, version.Minor, version.Build)
    };

    private Dictionary<string, List<BepInEx.PluginInfo>> chainloaderInputPlugins;

    private void Reset()
    {
        staticAssemblies = null;
        chainloaderInputPlugins = null;
        pluginsWithNoKnownIssues = null;
        skippedPlugins_ProcessMismatch = null;
        skippedPlugins_IncompatibilitiesPresent = null;
        skippedPlugins_SkippedDependencies = null;
        skippedPlugins_MissingDependencies = null;
        skippedPlugins_ReasonsUnknown = null;
        failedPlugins_CouldNotBeInstantiated = null;
        failedPlugins_MissingBepInDependencyAttributes = null;
        failedPlugins_ThrewInAwake = null;
        suspiciousPlugins_MissingReferences = null;
    }

    private IEnumerator Start()
    {
        if (!Chainloader._loaded)
        {
            Logger.LogInfo("Waiting for BepInEx to finish loading plugins...");
            yield return new WaitUntil(() => Chainloader._loaded);
        }

        using (var logListener = GlobalLogListener.Instance)
        {   // clean up our global log listener once we start our diagnosis
            if (logListener is not null)
            {
                BepInEx.Logging.Logger.Listeners.Remove(logListener);
            }
        }

        Logger.LogMessage("The Doctor is in.");

        PerformCheckup();
    }

    private BepInEx.PluginInfo GetLoadedPluginInfo(BepInEx.PluginInfo pluginInfo) =>
        Chainloader.PluginInfos.TryGetValue(pluginInfo.Metadata.GUID, out var pluginInfoOrNull) switch
        {
            _ => pluginInfoOrNull
        };

    private string GetPluginLocation(BepInEx.PluginInfo pluginInfo) => pluginInfo?.Location ??
        GetLoadedPluginInfo(pluginInfo)?.Location ??
        (chainloaderInputPlugins ?? GetChainloaderInputPlugins())
            .FirstOrDefault(entry => entry.Value.Select(p => p.Metadata.GUID).Contains(pluginInfo.Metadata.GUID)).Key;


    // get all plugins which BepInEx will have attempted to load
    private Dictionary<string, List<BepInEx.PluginInfo>> GetChainloaderInputPlugins() =>
        TypeLoader.FindPluginTypes
        (
            directory: Paths.PluginPath,
            typeSelector: Chainloader.ToPluginInfo,
            assemblyFilter: Chainloader.HasBepinPlugins,
            cacheName: "chainloader" // use the same cache as BepInEx chainloader for performance
        );

    public void PerformCheckup()
    {
        Reset();

        Logger.LogDebug("Performing check-up...");

        ThreadingHelper.Instance.StartAsyncInvoke(() =>
        {
            performCheckup();
            return HandleDiagnosis;
        });
    }

    private void performCheckup()
    {
        chainloaderInputPlugins = GetChainloaderInputPlugins();

        // create a guid lookup dictionary
        var pluginsByGuid = chainloaderInputPlugins.Values.SelectMany(plugin => plugin).ToDictionary(plugin => plugin.Metadata.GUID);

        foreach (var entry in chainloaderInputPlugins)
        {
            var location = entry.Key;
            var assembly = StaticAssemblies.SingleOrDefault(assembly => assembly.Location == location);
            var references = WalkReferences(assembly).ToLookup(reference => StaticAssemblies.Any(assembly => AssemblyName.ReferenceMatchesDefinition(reference, assembly.GetName())));
            var loadedReferences = references[true];
            var missingReferences = references[false];

            foreach (var plugin in entry.Value)
            {
                Version targetVersion = new(plugin.TargettedBepInExVersion.Major, plugin.TargettedBepInExVersion.Minor, Math.Max(0, plugin.TargettedBepInExVersion.Build));
                if (targetVersion.Major != BepInExVersion.Major || targetVersion > BepInExVersion)
                {   // plugin targets a potentially incompatible version of BepInEx

                    (suspiciousPlugins_TargetsWrongBepInExVersion ??= []).Add(plugin, targetVersion);
                }

                if (missingReferences.Any())
                {   // plugin has missing assembly references, probably the developer forgot to mark a plugin as a dependency
                    // might not have prevented the plugin from loading outright though - user should check if it seems to be working

                    (suspiciousPlugins_MissingReferences ??= []).Add(plugin, missingReferences);
                }

                if (!Chainloader.PluginInfos.ContainsKey(plugin.Metadata.GUID))
                {   // BepInEx skipped loading the plugin, try to determine why

                    var invalidProcess = plugin.Processes.Any() &&
                        plugin.Processes.All(process => !string.Equals(process.ProcessName.Replace(".exe", ""), Paths.ProcessName, StringComparison.InvariantCultureIgnoreCase));

                    if (invalidProcess)
                    {   // skipped due to process filters (game executable marked as incompatible)

                        (skippedPlugins_ProcessMismatch ??= []).Add(plugin, plugin.Processes);
                    }

                    var incompatiblePlugins = Chainloader.PluginInfos.Values
                        .Where(pluginInfo => plugin.Incompatibilities
                            .Select(incompatibility => incompatibility.IncompatibilityGUID)
                            .Contains(pluginInfo.Metadata.GUID));

                    if (incompatiblePlugins.Any())
                    {   // skipped due to presence of plugins which are marked as incompatible

                        (skippedPlugins_IncompatibilitiesPresent ??= []).Add(plugin, incompatiblePlugins);
                    }

                    var skippedDependencies = pluginsByGuid.Values
                        .Where(plugin => !Chainloader.PluginInfos.ContainsKey(plugin.Metadata.GUID) &&
                            plugin.Dependencies
                                .Where(dependency => (dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0)
                                .Select(dependency => dependency.DependencyGUID)
                                .Contains(plugin.Metadata.GUID));

                    if (skippedDependencies.Any())
                    {   // skipped due to dependencies which were skipped by bepinex (e.g. they're incompatible or something)

                        (skippedPlugins_SkippedDependencies ??= []).Add(plugin, skippedDependencies);
                    }

                    var missingDependencies = plugin.Dependencies
                        .Where(dependency => (dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0 &&
                            (!pluginsByGuid.TryGetValue(dependency.DependencyGUID, out var dep) ||
                            dependency.MinimumVersion > dep.Metadata.Version));

                    if (missingDependencies.Any())
                    {   // skipped due to missing dependencies
                        (skippedPlugins_MissingDependencies ??= []).Add(plugin, missingDependencies);
                    }

                    if (!invalidProcess && !incompatiblePlugins.Any() && !skippedDependencies.Any() && !missingDependencies.Any())
                    {   // couldn't determine why the plugin was skipped, suggest user ensures they have all dependencies installed

                        (skippedPlugins_ReasonsUnknown ??= []).Add(plugin);
                    }
                }
                else
                {
                    if (GetLoadedPluginInfo(plugin) is BepInEx.PluginInfo pluginInfo)
                    {
                        if (pluginInfo.Instance == null)
                        {   // either the plugin was intentionally destroyed, or Unity failed to instantiate it which usually indicates missing assembly references

                            if (Patcher.PluginTypeNames_CouldNotBeInstantiated?.TryGetValue(pluginInfo.TypeName, out var data) ?? false)
                            {   // seems like Unity failed to instantiate the plugin
                                // check if we already found missing references for this plugin

                                if (!missingReferences.Any())
                                {   // looks like we failed to find missing references somehow
                                    // 
                                    // this might mean that the reference actually exists, but it wasn't loaded in the AppDomain yet
                                    // when BepInEx tried to load the plugin, i.e. because the mod dev forgot to put a BepInDependency attribute

                                    var forgottenDependencies = loadedReferences
                                        .Select(reference => StaticAssemblies.SingleOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(reference, assembly.GetName())))
                                        .Where(assembly => assembly is not null)
                                        .SelectMany(assembly => chainloaderInputPlugins.TryGetValue(assembly.Location, out var pluginInfos) switch { _ => pluginInfos?.Select(pluginInfo => pluginInfo?.Metadata?.GUID) })
                                        .Where(guid => guid is not null && plugin.Dependencies.All(dependency => dependency.DependencyGUID != guid))
                                        .Select(guid => pluginsByGuid.TryGetValue(guid, out var pluginInfo) switch { _ => pluginInfo })
                                        .Where(pluginInfo => pluginInfo is not null);

                                    if (forgottenDependencies.Any())
                                    {   // looks like the plugin forgot to mark plugin assemblies as dependencies, so they weren't loaded in the AppDomain
                                        // when BepInEx tried to load the plugin. user will need to complain to the mod developer and get them to fix it
                                        // by adding BepInDependency attributes to the plugin

                                        (failedPlugins_MissingBepInDependencyAttributes ??= []).Add(plugin, forgottenDependencies);
                                    }
                                    else
                                    {   // looks like it failed to instantiate for unknown reasons. user will probably need to reach out to the mod developer to get it fixed,
                                        // but it still could be pebcak

                                        (failedPlugins_CouldNotBeInstantiated ??= []).Add(plugin, data);
                                    }
                                }
                            }
                            else
                            {   // looks like the plugin was intentionally destroyed, ignore it
                            }
                        }
                        else if (!pluginInfo.Instance.enabled)
                        {   // either the plugin was intentionally disabled (by itself or otherwise), or an error was thrown in Awake

                            if (Patcher.PluginTypeNames_ThrewInAwake?.TryGetValue(pluginInfo.TypeName, out var data) ?? false)
                            {   // looks like the plugin threw in Awake, without missing dependencies or missing references.
                                // it's quite likely then that the plugin is not compatible with the game (or game version)
                                // that the user is running.

                                (failedPlugins_ThrewInAwake ??= []).Add(plugin, data);
                            }
                            else
                            {   // looks like the plugin was intentionally disabled (by itself or otherwise), ignore it
                            }
                        }
                    }
                }

                if (!SymptomaticPlugins.Contains(plugin))
                {
                    (pluginsWithNoKnownIssues ??= []).Add(plugin);
                }
            }
        }
    }

    private IEnumerable<AssemblyName> WalkReferences(Assembly assembly, HashSet<AssemblyName> seen = null)
    {
        if (assembly is null) yield break;

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if ((seen ??= []).Any(a => AssemblyName.ReferenceMatchesDefinition(reference, a))) continue;

            Assembly referenceAssembly = null;
            try
            {
                referenceAssembly = StaticAssemblies.SingleOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), reference));
            }
            catch (InvalidOperationException)
            { }

            if (referenceAssembly is not null)
            {
                var name = referenceAssembly.GetName();
                (seen ??= []).Add(name);
                yield return name;
                foreach (var child in WalkReferences(referenceAssembly, seen)) yield return child;
            }
            else
            {
                (seen ??= []).Add(reference);
                yield return reference;
            }
        }
    }

    public void HandleDiagnosis()
    {
        try
        {
            LogReport();
        }
        finally
        {
            Cleanup();
        }
    }

    private void Cleanup()
    {
        chainloaderInputPlugins?.Clear();
        chainloaderInputPlugins = null;
    }

    public void LogReport()
    {
        Logger.LogDebug("Check-up completed.");

        Logger.LogMessage("|=====================  PLUGIN DOCTOR REPORT  =====================|");
        Logger.LogMessage(string.Empty);
        Logger.LogMessage("  PLUGINS SKIPPED BY BEPINEX");
        Logger.LogMessage(string.Empty);

        foreach (var pluginInfo in SkippedPlugins)
        {
            Logger.LogMessage($"  - {pluginInfo.Metadata.Name}");
            Logger.LogMessage($"      guid:     {pluginInfo.Metadata.GUID}");
            Logger.LogMessage($"      version:  {pluginInfo.Metadata.Version}");
            Logger.LogMessage($"      filename: {Path.GetFileName(GetPluginLocation(pluginInfo))}");
            Logger.LogMessage(string.Empty);
            Logger.LogMessage($"    SYMPTOMS");

            if (skippedPlugins_ProcessMismatch?.TryGetValue(pluginInfo, out var processes) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin requires game process to match at least one of the");
                Logger.LogError($"      following filters:");
                Logger.LogMessage(string.Empty);
                foreach (var process in processes)
                {
                    Logger.LogMessage($"      - {process.ProcessName}");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      \"{Paths.ProcessName}\" does not match this requirement.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should make sure the plugin is intended to be used");
                Logger.LogMessage($"      with this game, and reach out to the developer of the");
                Logger.LogMessage($"      plugin and kindly ask them to address the issue.");
            }

            if (skippedPlugins_IncompatibilitiesPresent?.TryGetValue(pluginInfo, out var incompatibilities) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin is marked as incompatible with the following:");
                Logger.LogMessage(string.Empty);
                foreach (var incompatibility in incompatibilities)
                {
                    Logger.LogMessage($"      - {incompatibility.Metadata.Name} [{incompatibility.Metadata.GUID}]");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient will need to choose which is more important,");
                Logger.LogMessage($"      {pluginInfo.Metadata.Name}, or");
                Logger.LogMessage($"      the above list, and delete the other(s).");
            }

            if (skippedPlugins_MissingDependencies?.TryGetValue(pluginInfo, out var missingDependencies) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin requires the following plugins which were not found:");
                Logger.LogMessage(string.Empty);
                foreach (var missingDependency in missingDependencies)
                {
                    Logger.LogMessage($"      - {missingDependency.DependencyGUID}{(missingDependency.MinimumVersion?.Equals(EmptyVersion) ?? true ? string.Empty : $" [>= {missingDependency.MinimumVersion}]")}");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should check the documentation of the plugin for");
                Logger.LogMessage($"      information on its requirements and where to obtain them,");
                Logger.LogMessage($"      and install them.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised to consult with the developer of the");
                Logger.LogMessage($"      plugin and/or the modding community if symptoms persist.");
            }

            if (skippedPlugins_SkippedDependencies?.TryGetValue(pluginInfo, out var skippedDependencies) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin requires the following plugins which were skipped:");
                Logger.LogMessage(string.Empty);
                foreach (var skippedDependency in skippedDependencies)
                {
                    Logger.LogMessage($"      - {skippedDependency.Metadata.Name} [{skippedDependency.Metadata.GUID}]");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should follow the treatment plan(s) for the above");
                Logger.LogMessage($"      listed plugins.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised to consult with the developer of the");
                Logger.LogMessage($"      plugin and/or the modding community if symptoms persist.");
            }

            if (skippedPlugins_ReasonsUnknown?.Contains(pluginInfo) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin was skipped for unknown reasons.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should consult with the developer of the plugin");
                Logger.LogMessage($"      and/or the modding community.");
            }

            Logger.LogMessage(string.Empty);
        }

        Logger.LogMessage("  PLUGINS WHICH FAILED TO INITIALISE");
        Logger.LogMessage(string.Empty);

        foreach (var pluginInfo in FailedPlugins)
        {
            Logger.LogMessage($"  - {pluginInfo.Metadata.Name}");
            Logger.LogMessage($"      guid:     {pluginInfo.Metadata.GUID}");
            Logger.LogMessage($"      version:  {pluginInfo.Metadata.Version}");
            Logger.LogMessage($"      filename: {Path.GetFileName(GetPluginLocation(pluginInfo))}");
            Logger.LogMessage(string.Empty);
            Logger.LogMessage($"    SYMPTOMS");

            if (FailedPlugins_MissingReferences.TryGetValue(pluginInfo, out var missingReferences))
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin has missing assembly references:");
                Logger.LogMessage(string.Empty);
                foreach (var missingReference in missingReferences)
                {
                    Logger.LogMessage($"      - {missingReference.Name} {missingReference.Version}");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should check the documentation of the plugin for");
                Logger.LogMessage($"      information on its requirements and where to obtain them,");
                Logger.LogMessage($"      and install them.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      additionally, patient is advised to reach out to the");
                Logger.LogMessage($"      developer of the plugin and inform them that their plugin");
                Logger.LogMessage($"      may be missing BepInDependency attribute(s), which could");
                Logger.LogMessage($"      cause load order issues.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised to consult with the developer of the");
                Logger.LogMessage($"      plugin and/or the modding community if symptoms persist.");
            }

            if (failedPlugins_MissingBepInDependencyAttributes?.TryGetValue(pluginInfo, out var pluginsNeedingDependencyAttributes) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin was loaded before its following requirements:");
                Logger.LogMessage(string.Empty);
                foreach (var pluginNeedingDependencyAttribute in pluginsNeedingDependencyAttributes)
                {
                    Logger.LogMessage($"      - {pluginNeedingDependencyAttribute.Metadata.Name} [{pluginNeedingDependencyAttribute.Metadata.GUID}]");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should reach out to the developer of the plugin");
                Logger.LogMessage($"      and kindly ask them to add BepInDependency attribute(s) to");
                Logger.LogMessage($"      the plugin's BaseUnityPlugin class with the above listed");
                Logger.LogMessage($"      plugin GUID(s).");
            }

            if (failedPlugins_ThrewInAwake?.TryGetValue(pluginInfo, out var data) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin threw an Exception during execution of its Awake");
                Logger.LogError($"      method with the following data:");
                Logger.LogMessage(string.Empty);
                foreach (var line in data.ToString().Split('\n').Select(l => l.TrimEnd('\r')))
                {
                    Logger.LogError($"        {line}");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      this symptom could indicate that the plugin is");
                Logger.LogMessage($"      incompatible with the current version of the game.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should check the documentation of the plugin for");
                Logger.LogMessage($"      indications of whether the plugin is intended for use on a");
                Logger.LogMessage($"      specific version of the game, and if so, patient should");
                Logger.LogMessage($"      consult with the developer of the plugin and/or the");
                Logger.LogMessage($"      modding community for advice on how to resolve this.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      otherwise, symptom may indicate that the plugin has a bug.");
                Logger.LogMessage($"      patient should consult with the developer of the plugin");
                Logger.LogMessage($"      and kindly ask them to address the issue.");
            }

            if (failedPlugins_CouldNotBeInstantiated?.TryGetValue(pluginInfo, out data) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogError($"      plugin could not be instantiated by unity, with the");
                Logger.LogError($"      following data:");
                Logger.LogError($"        ");
                Logger.LogMessage(string.Empty);
                foreach (var line in data.ToString().Split('\n').Select(l => l.TrimEnd('\r')))
                {
                    Logger.LogError($"        {line}");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    TREATMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient should consult with the developer of the plugin");
                Logger.LogMessage($"      and kindly ask them to address the issue.");
            }

            Logger.LogMessage(string.Empty);
        }

        Logger.LogMessage("  PLUGINS WITH POTENTIAL ISSUES");
        Logger.LogMessage(string.Empty);

        foreach (var pluginInfo in SuspiciousPlugins)
        {
            Logger.LogMessage($"  - {pluginInfo.Metadata.Name}");
            Logger.LogMessage($"      guid:     {pluginInfo.Metadata.GUID}");
            Logger.LogMessage($"      version:  {pluginInfo.Metadata.Version}");
            Logger.LogMessage($"      filename: {Path.GetFileName(GetPluginLocation(pluginInfo))}");
            Logger.LogMessage(string.Empty);
            Logger.LogMessage($"    SYMPTOMS");

            if (SuspiciousPlugins_MissingReferences.TryGetValue(pluginInfo, out var missingReferences))
            {
                Logger.LogMessage(string.Empty);
                Logger.LogWarning($"      plugin has missing assembly references:");
                Logger.LogMessage(string.Empty);
                foreach (var missingReference in missingReferences)
                {
                    Logger.LogMessage($"      - {missingReference.Name} {missingReference.Version}");
                }
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      this could be indicative of missing requirements, or they");
                Logger.LogMessage($"      could simply be optional dependencies.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    ADVISEMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised to check the documentation of the");
                Logger.LogMessage($"      plugin for its requirements and optional dependencies and");
                Logger.LogMessage($"      where to obtain them, and install them.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised to consult with the developer of the");
                Logger.LogMessage($"      plugin and/or the modding community if symptoms persist.");
            }

            if (suspiciousPlugins_TargetsWrongBepInExVersion?.TryGetValue(pluginInfo, out var targetVersion) ?? false)
            {
                Logger.LogMessage(string.Empty);
                Logger.LogWarning($"      plugin targets a potentially incompatible BepInEx version:");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"        expected:  {targetVersion}");
                Logger.LogMessage($"        installed: {BepInExVersion}");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"    ADVISEMENT");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised that unless the plugin is not");
                Logger.LogMessage($"      functioning as expected, they can safely ignore this");
                Logger.LogMessage($"      symptom.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      if there are other relevant symptoms for this plugin,");
                Logger.LogMessage($"      patient is advised to follow their relevant treatments or");
                Logger.LogMessage($"      advisements and ignore this symptom.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      otherwise, patient should update either the plugin or");
                Logger.LogMessage($"      their BepInEx installation as applicable.");
                Logger.LogMessage(string.Empty);
                Logger.LogMessage($"      patient is advised to consult with the developer of the");
                Logger.LogMessage($"      plugin and/or the modding community if symptoms persist.");
            }
            
            Logger.LogMessage(string.Empty);
        }

        Logger.LogMessage("  PLUGINS WITH NO KNOWN ISSUES");
        Logger.LogMessage(string.Empty);

        foreach (var pluginInfo in AsymptomaticPlugins)
        {
            Logger.LogMessage($"  - {pluginInfo.Metadata.Name}");
            Logger.LogMessage($"      guid:     {pluginInfo.Metadata.GUID}");
            Logger.LogMessage($"      version:  {pluginInfo.Metadata.Version}");
            Logger.LogMessage($"      filename: {Path.GetFileName(GetPluginLocation(pluginInfo))}");
            Logger.LogMessage(string.Empty);
        }

        Logger.LogMessage("|==================================================================|");
    }
}
