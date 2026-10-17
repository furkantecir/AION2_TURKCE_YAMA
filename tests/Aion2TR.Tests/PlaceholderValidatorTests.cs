using Aion2TR.Core.Services;
using Xunit;

namespace Aion2TR.Tests;

public class PlaceholderValidatorTests
{
    private readonly PlaceholderValidator _validator = new();

    [Fact]
    public void Validate_IdenticalPlaceholders_ReturnsSuccess()
    {
        var src = "Welcome %s, you have {0} coins! <color=red>Click here</color>";
        var tgt = "Hoş geldin %s, {0} altının var! <color=red>Buraya tıkla</color>";

        var res = _validator.Validate(src, tgt);
        Assert.True(res.IsValid);
        Assert.Empty(res.MissingInTarget);
        Assert.Empty(res.ExtraInTarget);
    }

    [Fact]
    public void Validate_MissingPlaceholder_ReturnsFailure()
    {
        var src = "Player {PlayerName} defeated %d monsters.";
        var tgt = "{PlayerName} adlı oyuncu canavarları yendi."; // missing %d

        var res = _validator.Validate(src, tgt);
        Assert.False(res.IsValid);
        Assert.Contains(res.MissingInTarget, m => m.Contains("%d"));
    }

    [Fact]
    public void Validate_ExtraPlaceholder_ReturnsFailure()
    {
        var src = "Simple text";
        var tgt = "Basit metin {0}"; // unexpected extra {0}

        var res = _validator.Validate(src, tgt);
        Assert.False(res.IsValid);
        Assert.Contains(res.ExtraInTarget, e => e.Contains("{0}"));
    }

    [Fact]
    public void ExtractPlaceholders_FindsAllFormats()
    {
        var text = "Hello %s! Score: %d. Target: {PlayerTarget}. Status: <b_font>OK</b_font>. Line:\nNext";
        var tokens = _validator.ExtractPlaceholders(text);

        Assert.Contains("%s", tokens);
        Assert.Contains("%d", tokens);
        Assert.Contains("{PlayerTarget}", tokens);
        Assert.Contains("<b_font>", tokens);
        Assert.Contains("</b_font>", tokens);
        Assert.Contains("\n", tokens);
    }

    [Fact]
    public void ProtectionService_IdentifiesProtectedItems()
    {
        var protection = new TranslationProtectionService();

        Assert.True(protection.Evaluate("KEY_1", "12345").IsProtected);
        Assert.True(protection.Evaluate("KEY_2", "+50%").IsProtected);
        Assert.True(protection.Evaluate("KEY_3", "---").IsProtected);
        Assert.True(protection.Evaluate("KEY_4", "https://aion2.plaync.com").IsProtected);
        Assert.True(protection.Evaluate("KEY_5", "/Game/UI/Textures/Icon.uasset").IsProtected);
        Assert.True(protection.Evaluate("KEY_6", "WBP_InventorySlot").IsProtected);
        Assert.True(protection.Evaluate("KEY_7", "{0}").IsProtected);
        Assert.True(protection.Evaluate("KEY_8", "<br>").IsProtected);

        // Real text should NOT be protected
        Assert.False(protection.Evaluate("UI_START", "Start Game").IsProtected);
        Assert.False(protection.Evaluate("QUEST_TITLE", "Defeat the Dragon").IsProtected);
    }

    [Fact]
    public void QualityValidator_DetectsIssues()
    {
        var qv = new QualityValidator();

        // Empty translation
        Assert.False(qv.Validate("Hello", "").IsValid);

        // Broken markup
        Assert.False(qv.Validate("Click <color=red>here</color>", "Tıkla <color=red>buraya").IsValid);

        // Identical natural language
        Assert.False(qv.Validate("Are you sure you want to quit?", "Are you sure you want to quit?").IsValid);

        // Valid translation
        Assert.True(qv.Validate("Are you sure you want to quit?", "Çıkmak istediğinize emin misiniz?").IsValid);
    }

    [Fact]
    public void ContextService_InfersCategoriesCorrectly()
    {
        var ctx = new TranslationContextService();

        Assert.Equal("UI", ctx.InferCategory("UI_BTN_START"));
        Assert.Equal("Quest", ctx.InferCategory("QUEST_MAIN_01"));
        Assert.Equal("Item", ctx.InferCategory("ITEM_SWORD_09"));
        Assert.Equal("Skill", ctx.InferCategory("SKILL_FIREBALL"));
        Assert.Equal("NPC", ctx.InferCategory("NPC_MERCHANT_01"));
        Assert.Equal("System", ctx.InferCategory("SYS_SERVER_DISCONNECT"));
        Assert.Equal("Unknown", ctx.InferCategory("SOME_RANDOM_CODE"));
    }
}
