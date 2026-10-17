using System;
using System.Text.RegularExpressions;

namespace Aion2TR.Core.Services;

public interface ITranslationContextService
{
    string InferCategory(string key);
}

public class TranslationContextService : ITranslationContextService
{
    public string InferCategory(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Unknown";

        string upper = key.ToUpperInvariant();

        if (upper.StartsWith("UI_") || upper.Contains("_UI_") || upper.Contains("WIDGET") || upper.Contains("BUTTON") || upper.Contains("MENU") || upper.Contains("TOOLTIP"))
            return "UI";

        if (upper.StartsWith("QUEST_") || upper.Contains("_QUEST_") || upper.StartsWith("Q_") || upper.Contains("MISSION"))
            return "Quest";

        if (upper.StartsWith("ITEM_") || upper.Contains("_ITEM_") || upper.Contains("EQUIP") || upper.Contains("WEAPON") || upper.Contains("ARMOR"))
            return "Item";

        if (upper.StartsWith("SKILL_") || upper.Contains("_SKILL_") || upper.Contains("ABILITY") || upper.Contains("SPELL"))
            return "Skill";

        if (upper.StartsWith("NPC_") || upper.Contains("_NPC_") || upper.StartsWith("MONSTER_") || upper.Contains("CREATURE"))
            return "NPC";

        if (upper.StartsWith("SYS_") || upper.StartsWith("SYSTEM_") || upper.Contains("_SYSTEM_") || upper.Contains("ERROR") || upper.Contains("SERVER"))
            return "System";

        if (upper.StartsWith("TUTORIAL_") || upper.Contains("TUTORIAL") || upper.Contains("GUIDE"))
            return "Tutorial";

        if (upper.StartsWith("DIALOG_") || upper.StartsWith("DIALOGUE_") || upper.Contains("_DIALOG_") || upper.Contains("TALK"))
            return "Dialogue";

        if (upper.StartsWith("ACHIEVE_") || upper.StartsWith("ACHIEVEMENT_") || upper.Contains("TROPHY"))
            return "Achievement";

        if (upper.StartsWith("CHAR_") || upper.StartsWith("CHARACTER_") || upper.Contains("CLASS_") || upper.Contains("RACE_"))
            return "Character";

        if (upper.StartsWith("WORLD_") || upper.StartsWith("ZONE_") || upper.StartsWith("MAP_") || upper.StartsWith("REGION_"))
            return "World";

        return "Unknown";
    }
}
