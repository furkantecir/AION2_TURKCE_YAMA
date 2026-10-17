# AION 2 Community Localization Pipelines Analysis

This document provides a technical evaluation of community tools and pipelines developed for AION 2 localization:
1. **Holastor / AION-2-Localization** (RU/EN PO Pipeline)
2. **duoluoyuji / Aion2-Steam-CN** (Chinese Loose File Injection Script)

---

## 1. Holastor / AION-2-Localization Analysis

- **Repository**: `https://github.com/Holastor/AION-2-Localization`
- **Target Locale**: Russian (`ru-RU`) / English (`en-US`)

### A. Architecture & Workflow
The Holastor pipeline focuses on crowd-translation workflows using standard Gettext PO format:
```
[Unreal Engine Game Files / PAK]
               ↓ (Extract)
      L10NString.dat
               ↓ (DAT → PO tool)
      en_US.po / ru_RU.po
               ↓ (Crowdin / Weblate / Local Translation)
      Translated .po files
               ↓ (PO → DAT tool)
    Modified L10NString.dat
               ↓ (Game Mount / Override)
  Aion2/Content/L10N/Text/en-US/L10NString.dat
```

### B. DAT ↔ PO Conversion Details
1. **DAT → PO**:
   - The tool extracts the raw table entries `(Key, Text)` from decompressed `L10NString.dat`.
   - Each entry is mapped to Gettext format:
     - `msgctxt`: Unique Key string (e.g., `SkillString_STR_SKILL_PC_CHANTER_18010230_skill_spec_effect` or `String_STR_ITEM_AGITDECO_FRAME_172A_F_DESC_body`).
     - `msgid`: Source English text.
     - `msgstr`: Target translated text.
2. **PO → DAT**:
   - Compiles `.po` back into the Unreal Engine binary archive map `TMap<FString, FString>`.
   - Prepend Table Version (int32 = 1) and Namespace FString ("AION2").
   - Encrypts/compresses with NCSoft's container format (or mounts uncompressed if engine permits loose raw DAT, see Section 2).
3. **ID / Key Mapping Preservation**:
   - Keys in AION 2 are human-readable identifiers prefixed by category (`SkillString_`, `QuestString_`, `AchievementString_`, `CutsceneSubtitle_`, `Message_`).
   - By using `Key` as the unique index (`msgctxt`), game updates that add or reorder strings do NOT invalidate existing translations.
4. **Game Update Merging**:
   - Standard `msgmerge` (Gettext utility) is used when a new game patch is released:
     ```bash
     msgmerge --update old_translations.po new_source.pot
     ```
   - Untranslated new entries appear as empty `msgstr ""`, while modified source texts are marked with `#, fuzzy`.

---

## 2. Chinese Patch (duoluoyuji / Aion2-Steam-CN) Analysis

- **Repository**: `https://github.com/duoluoyuji/Aion2-Steam-CN`
- **Script**: `Aion2CNTool.ps1`

### A. Override Mechanism
The script discovers how Unreal Engine 5.3 handles loose file precedence:
1. In standard Unreal Engine, files located in the content folder (`Content/...`) will take priority over packaged files in `.pak` / `.ucas` / `.utoc` **IF** loose file loading is enabled or if the corresponding PAK chunk is absent.
2. The Chinese patch places the localized file at:
   ```
   Aion2\Content\L10N\Text\en-US\L10NString.dat
   ```
3. To force the engine to load this loose file instead of reading from the official localized PAK, the script performs intrusive steps:
   - Renaming or overwriting `Aion2\Content\Paks\L10N\Text\en-US\pakchunk0-WindowsClient.pak`
   - In some variants: Deleting or blanking `.sig`, `.utoc`, `.ucas` signature pairs.

### B. Security & Integrity Risk Assessment for Aion2TR
| Behavior | Aion2-Steam-CN | Aion2TR Policy | Reason |
|---|---|---|---|
| Read L10NString.dat from PAK | Supported | Supported | Safe read-only extraction |
| Loose file injection | Used | Planned for Phase 5 | Clean, no original file modification |
| Delete `.sig` files | Used in script | **STRICTLY FORBIDDEN** | Triggers Steam integrity failure & anti-cheat alert |
| Delete `.utoc` / `.ucas` | Used in script | **STRICTLY FORBIDDEN** | Corrupts game chunk catalog |
| Overwrite official PAK directly | Used in script | **STRICTLY FORBIDDEN** | Irreversible without Steam file verification |

### C. Takeaways for Aion2TR
1. The loose file path `Aion2\Content\L10N\Text\en-US\L10NString.dat` is **100% verified** by both Holastor and Aion2-Steam-CN as the engine's localization lookup point.
2. Aion2TR will adopt the non-destructive approach:
   - Keep all official `.pak`, `.sig`, `.utoc`, `.ucas` files 100% intact.
   - When installing the patch, write only to the loose folder with atomic backup and one-click uninstall.
