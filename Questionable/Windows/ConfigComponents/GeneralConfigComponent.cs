using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using static Questionable.Utils.LocalizeShortcut;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Questionable.Windows.ConfigComponents;

internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static readonly List<(uint Id, string Name)> DefaultMounts = [(0, _L("Mount Roulette"))];
    private static readonly List<(EClassJob ClassJob, string Name)> DefaultClassJobs = [(EClassJob.Adventurer, _L("Auto (highest level/item level)"))];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;

    private readonly uint[] _mountIds;
    private readonly string[] _mountNames;
    private readonly string[] _combatModuleNames = [_L("None"), "Boss Mod (VBM)", "Wrath Combo", "Rotation Solver Reborn"];

    private readonly string[] _grandCompanyNames =
        [_L("None (manually pick quest)"), _L("Maelstrom"), _L("Twin Adder"), _L("Immortal Flames")];

    private readonly EClassJob[] _classJobIds;
    private readonly string[] _classJobNames;

    public GeneralConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        QuestRegistry questRegistry,
        TerritoryData territoryData)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _territoryData = territoryData;

        var mounts = dataManager.GetExcelSheet<Mount>()
            .Where(x => x is { RowId: > 0, Icon: > 0 })
            .Select(x => (MountId: x.RowId, Name: x.Singular.ToString()))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .OrderBy(x => x.Name)
            .ToList();
        _mountIds = DefaultMounts.Select(x => x.Id).Concat(mounts.Select(x => x.MountId)).ToArray();
        _mountNames = DefaultMounts.Select(x => x.Name).Concat(mounts.Select(x => x.Name)).ToArray();

        var sortedClassJobs = classJobUtils.SortedClassJobs.Select(x => x.ClassJob).ToList();
        var classJobs = Enum.GetValues<EClassJob>()
            .Where(x => x != EClassJob.Adventurer)
            .Where(x => !x.IsCrafter() && !x.IsGatherer())
            .Where(x => !x.IsClass())
            .OrderBy(x => sortedClassJobs.IndexOf(x))
            .ToList();
        _classJobIds = DefaultClassJobs.Select(x => x.ClassJob).Concat(classJobs).ToArray();
        _classJobNames = DefaultClassJobs.Select(x => x.Name).Concat(classJobs.Select(x => _L(x.ToFriendlyString()))).ToArray();
    }

    private static readonly string[] LanguageCodes = ["en", "ja-jp", "zh-cn", "zh-tw"];

    public override void DrawTab()
    {
        using var tab = ImRaii.TabItem(_L("General") + "###General");
        if (!tab)
            return;

        string[] languageNames =
        [
            _L("English"),
            _L("Japanese"),
            _L("Chinese (Simplified)"),
            _L("Chinese (Traditional)"),
        ];
        int selectedLanguage = Array.IndexOf(LanguageCodes, Configuration.General.Language);
        if (selectedLanguage == -1)
            selectedLanguage = 0;
        if (ImGui.Combo(_L("Language"), ref selectedLanguage, languageNames, languageNames.Length))
        {
            string was = Configuration.General.Language;
            Configuration.General.Language = LanguageCodes[selectedLanguage];
            Save();
            if (was != Configuration.General.Language)
                DalamudInitializer.SetupI18N(Configuration.General.Language);
        }

        {
            int selectedCombatModule = (int)Configuration.General.CombatModule;
            if (ImGui.Combo(_L("Preferred Combat Module"), ref selectedCombatModule, _combatModuleNames,
                    _combatModuleNames.Length))
            {
                Configuration.General.CombatModule = (Configuration.ECombatModule)selectedCombatModule;
                Save();
            }
        }

        int selectedMount = Array.FindIndex(_mountIds, x => x == Configuration.General.MountId);
        if (selectedMount == -1)
        {
            selectedMount = 0;
            Configuration.General.MountId = _mountIds[selectedMount];
            Save();
        }

        if (ImGui.Combo(_L("Preferred Mount"), ref selectedMount, _mountNames, _mountNames.Length))
        {
            Configuration.General.MountId = _mountIds[selectedMount];
            Save();
        }

        int grandCompany = (int)Configuration.General.GrandCompany;
        if (ImGui.Combo(_L("Preferred Grand Company"), ref grandCompany, _grandCompanyNames,
                _grandCompanyNames.Length))
        {
            Configuration.General.GrandCompany = (GrandCompany)grandCompany;
            Save();
        }

        int combatJob = Array.IndexOf(_classJobIds, Configuration.General.CombatJob);
        if (combatJob == -1)
        {
            Configuration.General.CombatJob = EClassJob.Adventurer;
            Save();

            combatJob = 0;
        }

        if (ImGui.Combo(_L("Preferred Combat Job"), ref combatJob, _classJobNames, _classJobNames.Length))
        {
            Configuration.General.CombatJob = _classJobIds[combatJob];
            Save();
        }

        ImGui.Separator();
        ImGui.Text(_L("UI"));
        using (ImRaii.PushIndent())
        {
            bool hideInAllInstances = Configuration.General.HideInAllInstances;
            if (ImGui.Checkbox(_L("Hide quest window in all instanced duties"), ref hideInAllInstances))
            {
                Configuration.General.HideInAllInstances = hideInAllInstances;
                Save();
            }

            bool useEscToCancelQuesting = Configuration.General.UseEscToCancelQuesting;
            if (ImGui.Checkbox(_L("Use ESC to cancel questing/movement"), ref useEscToCancelQuesting))
            {
                Configuration.General.UseEscToCancelQuesting = useEscToCancelQuesting;
                Save();
            }

            bool showIncompleteSeasonalEvents = Configuration.General.ShowIncompleteSeasonalEvents;
            if (ImGui.Checkbox(_L("Show details for incomplete seasonal events"), ref showIncompleteSeasonalEvents))
            {
                Configuration.General.ShowIncompleteSeasonalEvents = showIncompleteSeasonalEvents;
                Save();
            }
        }

        ImGui.Separator();
        ImGui.Text(_L("Questing"));
        using (ImRaii.PushIndent())
        {
            bool configureTextAdvance = Configuration.General.ConfigureTextAdvance;
            if (ImGui.Checkbox(_L("Automatically configure TextAdvance with the recommended settings"),
                    ref configureTextAdvance))
            {
                Configuration.General.ConfigureTextAdvance = configureTextAdvance;
                Save();
            }

            bool skipLowPriorityInstances = Configuration.General.SkipLowPriorityDuties;
            if (ImGui.Checkbox(_L("Unlock certain optional dungeons and raids (instead of waiting for completion)"), ref skipLowPriorityInstances))
            {
                Configuration.General.SkipLowPriorityDuties = skipLowPriorityInstances;
                Save();
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("Questionable automatically picks up some optional quests (e.g. for aether currents, or the ARR alliance raids)."));
                    ImGui.Text(_L("If this setting is enabled, Questionable will continue with other quests, instead of waiting for manual completion of the duty."));

                    ImGui.Separator();
                    ImGui.Text(_L("This affects the following dungeons and raids:"));
                    foreach (var lowPriorityCfc in _questRegistry.LowPriorityContentFinderConditionQuests)
                    {
                        if (_territoryData.TryGetContentFinderCondition(lowPriorityCfc.ContentFinderConditionId, out var cfcData))
                        {
                            ImGui.BulletText($"{cfcData.Name}");
                        }
                    }
                }
            }

            bool autoRetryOnStuck = Configuration.General.AutoRetryOnStuck;
            if (ImGui.Checkbox(_L("Automatically retry the current step when stuck"), ref autoRetryOnStuck))
            {
                Configuration.General.AutoRetryOnStuck = autoRetryOnStuck;
                Save();
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("If no progress is made for the configured time, Questionable restarts the current quest step."));
                    ImGui.Text(_L("Waits for cutscenes, dialogue, combat, movement and duties never count as being stuck."));
                    ImGui.Text(_L("After 3 unsuccessful retries on the same step, questing stops with an error."));
                }
            }

            if (autoRetryOnStuck)
            {
                using (ImRaii.PushIndent())
                {
                    int stuckThreshold = Configuration.General.StuckRetryThresholdSeconds;
                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt(_L("Stuck detection threshold (seconds)"), ref stuckThreshold, 30, 180))
                    {
                        Configuration.General.StuckRetryThresholdSeconds = stuckThreshold;
                        Save();
                    }
                }
            }
        }
    }
}
