using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ImGuiNET;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
using static Questionable.Utils.LocalizeShortcut;

namespace Questionable.Windows.ConfigComponents;

internal sealed class StopConditionComponent : ConfigComponent
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelector _acceptQuestSelector;
    private readonly QuestSelector _completeQuestSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;

    public StopConditionComponent(
        IDalamudPluginInterface pluginInterface,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        QuestTooltipComponent questTooltipComponent,
        UiUtils uiUtils,
        Configuration configuration)
        : base(pluginInterface, configuration)
    {
        _pluginInterface = pluginInterface;
        _questRegistry = questRegistry;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;

        _completeQuestSelector = new QuestSelector(questRegistry)
        {
            SuggestionPredicate = quest => configuration.Stop.QuestsToStopAfter.TrueForAll(x => x != quest.Id),
            DefaultPredicate = quest =>
                quest.Info.IsMainScenarioQuest && questFunctions.IsQuestAccepted(quest.Id),
            QuestSelected = quest =>
            {
                configuration.Stop.QuestsToStopAfter.Add(quest.Id);
                Save();
            },
        };

        _acceptQuestSelector = new QuestSelector(questRegistry)
        {
            SuggestionPredicate = quest => configuration.Stop.QuestsToStopWhenAccepted.TrueForAll(x => x != quest.Id),
            DefaultPredicate = quest =>
                quest.Info.IsMainScenarioQuest && !questFunctions.IsQuestAcceptedOrComplete(quest.Id),
            QuestSelected = quest =>
            {
                configuration.Stop.QuestsToStopWhenAccepted.Add(quest.Id);
                Save();
            },
        };
    }

    public override void DrawTab()
    {
        using var tab = ImRaii.TabItem(_L("Stop") + "###StopConditions");
        if (!tab)
            return;

        bool runCommand = Configuration.Stop.RunCommandAfterStop;
        if (ImGui.Checkbox(_L("Run command when Questionable finishes automatic questing"), ref runCommand))
        {
            Configuration.Stop.RunCommandAfterStop = runCommand;
            Save();
        }

        string command = Configuration.Stop.CommandAfterStop;
        if (ImGui.InputText(_L("Command"), ref command, 128))
        {
            Configuration.Stop.CommandAfterStop = command;
            Save();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(Configuration.Stop.CommandAfterStop))
                Configuration.Stop.CommandAfterStop = "/li auto";
            Save();
        }

        ImGui.Separator();

        bool enabled = Configuration.Stop.Enabled;
        if (ImGui.Checkbox(_L("Stop Questionable when any of the conditions below are met"), ref enabled))
        {
            Configuration.Stop.Enabled = enabled;
            Save();
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!enabled))
        {
            bool levelToStopAfter = Configuration.Stop.LevelToStopAfter;
            if (ImGui.Checkbox(_L("Enable level stop condition"), ref levelToStopAfter))
            {
                Configuration.Stop.LevelToStopAfter = levelToStopAfter;
                Save();
            }

            using (ImRaii.Disabled(!levelToStopAfter))
            {
                int targetLevel = Configuration.Stop.TargetLevel;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt(_L("Stop at level"), ref targetLevel, 1, 5))
                {
                    Configuration.Stop.TargetLevel = Math.Clamp(targetLevel, 1, 100);
                    Save();
                }

                unsafe
                {
                    short currentLevel = PlayerState.Instance()->CurrentLevel;
                    if (currentLevel > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled(_LF("(Current: {0})", currentLevel));
                    }
                }
            }

            ImGui.Separator();

            DrawQuestStopSection(
                _L("Stop when completing any of the quests selected below:"),
                "Complete",
                _completeQuestSelector,
                Configuration.Stop.QuestsToStopAfter);

            ImGui.Separator();

            DrawQuestStopSection(
                _L("Stop when accepting any of the quests selected below:"),
                "Accept",
                _acceptQuestSelector,
                Configuration.Stop.QuestsToStopWhenAccepted);
        }
    }

    private void DrawQuestStopSection(string label, string sectionId, QuestSelector selector, List<ElementId> quests)
    {
        using (ImRaii.PushId(sectionId))
        {
            ImGui.Text(label);
            selector.DrawSelection();

            if (quests.Count > 0)
            {
                using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, _L("Clear")))
                    {
                        quests.Clear();
                        Save();
                    }
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(_L("Hold CTRL to enable this button."));

                ImGui.Separator();
            }

            Quest? itemToRemove = null;
            for (int i = 0; i < quests.Count; i++)
            {
                ElementId questId = quests[i];

                if (!_questRegistry.TryGetQuest(questId, out Quest? quest))
                    continue;

                using (ImRaii.PushId($"Quest{questId}"))
                {
                    (Vector4 color, FontAwesomeIcon icon, string _) = _uiUtils.GetQuestStyle(questId);
                    bool hovered;
                    using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(color, icon.ToIconString());
                        hovered = ImGui.IsItemHovered();
                    }

                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(quest.Info.Name);
                    hovered |= ImGui.IsItemHovered();

                    if (hovered)
                        _questTooltipComponent.Draw(quest.Info);

                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * 2);
                    }

                    if (ImGuiComponents.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
                        itemToRemove = quest;
                }
            }

            if (itemToRemove != null)
            {
                quests.Remove(itemToRemove.Id);
                Save();
            }
        }
    }
}
