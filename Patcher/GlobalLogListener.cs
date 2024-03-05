using BepInEx.Logging;
using System;
using System.Linq;

namespace Tobey.PluginDoctor;
internal sealed class GlobalLogListener : ILogListener
{
    private static string UNITY_AWAKE_THROW_MESSAGE_END =
        """
        UnityEngine.GameObject:AddComponent(Type)
        BepInEx.Bootstrap.Chainloader:Start()
        UnityEngine.Application:.cctor()
        UWE.GameApplication:AppAwake()
        """;

    private GlobalLogListener() { }
    public static GlobalLogListener Instance { get; private set; } = new();

    public void Dispose()
    {
        UNITY_AWAKE_THROW_MESSAGE_END = null;
        Instance = null;
    }

    public void LogEvent(object _, LogEventArgs logEventArgs)
    {
        var source = logEventArgs.Source;
        var level = logEventArgs.Level;
        var data = logEventArgs.Data;

        if (source.SourceName != "Unity Log") return;

        var message = data.ToString().Trim();

        if (level == LogLevel.Warning && message.StartsWith("The script '") && message.EndsWith("' could not be instantiated!"))
        {
            var split = message.Split('\'');
            // the reason I'm not just grabbing split[1] is in case some weirdo includes an apostrophe in the name of their script
            var pluginTypeName = string.Join("'", split.Skip(1).Take(split.Length - 2).ToArray());

            (Patcher.PluginTypeNames_CouldNotBeInstantiated ??= []).Add(pluginTypeName, data);
        }
        else if (level == LogLevel.Error && message.EndsWith(UNITY_AWAKE_THROW_MESSAGE_END) && message.IndexOf("Stack trace:") is int stackTraceIndex && stackTraceIndex >= 0)
        {
            var split = message.Split(["Stack trace:", ".Awake () (at <", UNITY_AWAKE_THROW_MESSAGE_END], StringSplitOptions.RemoveEmptyEntries);
            var pluginTypeName = split[1].Trim();

            (Patcher.PluginTypeNames_ThrewInAwake ??= []).Add(pluginTypeName, data);
        }
    }
}
