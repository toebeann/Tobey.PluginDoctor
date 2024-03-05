# Plugin Doctor Roadmap

## Short-term

1. design and implement in-game GUI to display report to user in a nice format, with buttons to copy diagnoses,
   treatments and advisements to the clipboard in markdown syntax for easy pasting into e.g. discord, github issues,
   etc.
   
2. offer to treat the following types of issues:
   
    - mod developer forgot to include `BepInDependency` attribute in their plugin: we can attempt to fix this by
      patching the plugin .dll to have a `BepInDependency` attribute as needed with Mono.Cecil in a preloader patcher.
      user will need to restart the game for the patch to be applied.
      
    - `BepInProcess` attributes preventing plugin from loading: we can attempt to fix this by patching the plugin .dll
      to remove the `BepInProcess` filters already applied to the plugin with Mono.Cecil in a preloader patcher. user
      should be advised that if the plugin was designed for use with another game, this likely won't work, and may even
      cause issues. user will need to restart the game for the patch to be applied.
      
    the above treatments should be reversible, i.e. we should make backups of any .dlls before modifying them and
    offering to undo the patch at the user's request, especially in the case that symptoms persist with the plugin.
   
3. design a serialization format to save report, likely as json, so that it can be loaded and parsed by external tools,
   e.g. vortex, discord bots, etc.
   
## Long-term

-   implement an API for other plugins to register diagnoses, treatments and advisements.
    
-   implement a feature to analyse how a plugin interacts with the managed assemblies provided by the game, to detect
    if there are any compatibility issues between the types provided and the types expected. if it is performant enough,
    we could analyse every plugin assembly on start-up for these types of issues. otherwise, only analyse them if it is
    suspected to be the cause of an issue, e.g. by offering to analyse specific assemblies only.
    
    this feature would require loading up Mono.Cecil and doing a bunch of complex meta-programming wizardry, so will
    likely be quite time-consuming and complicated to prototype and implement.
    
    however, i believe it will be worth the effort, as it would be extremely useful for users to immediately know that
    the reason a mod isn't working is because they are using it on the wrong game, or wrong game version. it won't
    easily be possible to detect these kinds of issues outside the scope of `Awake` otherwise.
    
    an alternative could be to parse logs for errors about Type errors, but would need a large sample of relevant log
    messages, and it will likely not be foolproof, whereas analysing the assemblies I believe would have a much higher
    success rate, and this would still be limited to Type errors within `Awake` unless we want to continue monitoring
    log messages continously while the game is being played, which would impose an ongoing runtime performance cost.
