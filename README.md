# Tobey's Plugin Doctor for BepInEx 5

_**The Doctor is in.**_

Plugin Doctor will diagnose and report common symptoms patients might be facing with their BepInEx 5 plugins,
explaining them in simple terms and advising how to treat them.

## Diagnoses

Plugin Doctor can diagnose and advise how to treat (and when to ignore) the following symptoms:

-   plugins skipped by BepInEx for the following reasons:
    -   the game executable is considered incompatible by the developer of the plugin
    -   plugins marked as incompatible by the developer are installed
    -   requirements of the plugin are not installed or were skipped
    -   the plugin was otherwise skipped
-   plugins which failed during initialisation:
    -   the plugin was loaded before its requirements
    -   the plugin references assemblies which were not found
    -   the plugin otherwise failed during initialisation
-   plugins with potential issues:
    -   the plugin references assemblies which were not found, but did not fail during initialisation
    -   the plugin targets a potentially incompatible version of BepInEx

In the future, it is planned for Plugin Doctor to offer to treat some of these symptoms itself, where possible.

## Samples

[Sample reports can be found in the `samples` folder of Plugin Doctor's GitHub repository.](https://github.com/toebeann/Tobey.PluginDoctor/tree/main/samples)

## Installation

Download the latest release from [the GitHub releases page](https://github.com/toebeann/Tobey.PluginDoctor/releases),
then install it with the BepInEx mod manager of your choice, or just plop its contents into your game folder (after
installing [BepInEx](https://github.com/BepInEx/BepInEx), of course).

## Usage

Plugin Doctor will run automatically whenever you start the game, and print a report in the logs of any issues found.

To access the report, simply load the file `BepInEx/LogOutput.log` in the text-editor of your choice, and search for
"PLUGIN DOCTOR REPORT" (with Ctrl+F where available).

In the future, it is planned for Plugin Doctor to provide an in-game GUI for easy access to diagnoses, treatments and
advisements.

## Roadmap

[A roadmap of planned features can be found here.](https://github.com/toebeann/Tobey.PluginDoctor/blob/main/ROADMAP.md)
