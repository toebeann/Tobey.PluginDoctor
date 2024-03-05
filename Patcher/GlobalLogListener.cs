using BepInEx.Logging;
using System;
using System.Linq;

namespace Tobey.PluginDoctor;
internal sealed class GlobalLogListener : ILogListener
{
    private string[] BEPINEX_PLUGIN_LOADING_PARTS = ["Loading [", "]"];

    private GlobalLogListener() { }
    public static GlobalLogListener Instance { get; private set; } = new();

    public void Dispose() => Instance = null;

    private string lastBepInExPluginLoad;

    public void LogEvent(object _, LogEventArgs logEventArgs)
    {
        var source = logEventArgs.Source;
        var level = logEventArgs.Level;
        var data = logEventArgs.Data;
        var message = data.ToString().Trim();
        var lines = message.Split(['\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToList();

        switch (source.SourceName)
        {
            case "BepInEx"
            when level == LogLevel.Info && message.StartsWith(BEPINEX_PLUGIN_LOADING_PARTS.First()) && message.EndsWith(BEPINEX_PLUGIN_LOADING_PARTS.Last()):

                lastBepInExPluginLoad = message.Split(BEPINEX_PLUGIN_LOADING_PARTS, StringSplitOptions.RemoveEmptyEntries).SingleOrDefault();

                break;

            case "Unity Log"
            when lastBepInExPluginLoad is not null && (!(Patcher.PluginMetadata_ThrewInInit?.ContainsKey(lastBepInExPluginLoad)) ?? true) &&
                level == LogLevel.Warning && message.StartsWith("The script '") && message.EndsWith("' could not be instantiated!"):

                (Patcher.PluginMetadata_CouldNotBeInstantiated ??= []).Add(lastBepInExPluginLoad, data);

                lastBepInExPluginLoad = null;

                break;

            case "Unity Log"
            when lastBepInExPluginLoad is not null && (!(Patcher.PluginMetadata_ThrewInInit?.ContainsKey(lastBepInExPluginLoad)) ?? true) &&
                level == LogLevel.Error &&
                lines.LastIndexOf("Stack trace:") is int stackTraceIndex && stackTraceIndex >= 0 &&
                lines.LastIndexOf("UnityEngine.GameObject:AddComponent(Type)") is int addComponentIndex && addComponentIndex > stackTraceIndex &&
                lines.LastIndexOf("BepInEx.Bootstrap.Chainloader:Start()") is int chainloaderStartIndex && chainloaderStartIndex > addComponentIndex:

                (Patcher.PluginMetadata_ThrewInInit ??= []).Add(lastBepInExPluginLoad, data);

                lastBepInExPluginLoad = null;

                break;
        }
    }
}
