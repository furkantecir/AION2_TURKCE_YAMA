using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using K4os.Compression.LZ4;

namespace Aion2TR.Phase3Analyzer;

public static class UpdateTranslationApplier
{
    private static readonly byte[] NewOfficialPrefix32 = Convert.FromHexString("92A22F21AB3900B391A43790276063EC717E83198BEA40EDD5FB8B00E3A35B79");

    public static async Task ApplyAndBuildAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("================================================================================");
        Console.WriteLine("    AION 2 — APPLY TRANSLATION DELTA & COMPILE OFFICIAL V1.0.1 CONTAINER       ");
        Console.WriteLine("================================================================================");

        string repoRoot = Phase5PatchService.FindRepoRoot();
        string astraAuditDir = Path.Combine(repoRoot, "analysis", "astra_audit");
        string newOfficialPlainPath = Path.Combine(astraAuditDir, "official_updated_candidate.plain");
        string prevTurkishPlainPath = Path.Combine(repoRoot, "v2", "artifacts", "step8", "L10NString.tr-TR.full-candidate.plain");
        string manifestPath = Path.Combine(astraAuditDir, "official_key_manifest.dat");

        if (!File.Exists(newOfficialPlainPath))
        {
            Console.WriteLine($"[ERROR] New official plain table not found: {newOfficialPlainPath}");
            return;
        }

        var parser = new L10NStringParser();
        byte[] newOfficialPlainBytes = await File.ReadAllBytesAsync(newOfficialPlainPath);
        var newOfficialParse = parser.ParseRawTable(newOfficialPlainBytes, "NewOfficial");
        Console.WriteLine($"New Official Entries: {newOfficialParse.EntryCount:N0} rows");

        byte[] prevTurkishPlainBytes = await File.ReadAllBytesAsync(prevTurkishPlainPath);
        var prevTurkishParse = parser.ParseRawTable(prevTurkishPlainBytes, "PrevTurkish");
        Console.WriteLine($"Previous Turkish Entries: {prevTurkishParse.EntryCount:N0} rows");

        var prevTurkishMap = prevTurkishParse.Entries.ToDictionary(e => e.Key ?? "", e => e.Text);

        // Build translation dictionary for delta
        var deltaTranslations = BuildDeltaTranslations();
        Console.WriteLine($"Delta Translations Dictionary defined: {deltaTranslations.Count} translations");

        // Merge into new table preserving key order 100%
        var mergedEntries = new List<L10NEntry>(newOfficialParse.Entries.Count);
        int preservedCount = 0;
        int deltaAppliedCount = 0;
        int fallbackEnglishCount = 0;

        for (int i = 0; i < newOfficialParse.Entries.Count; i++)
        {
            var officialEntry = newOfficialParse.Entries[i];
            string key = officialEntry.Key ?? "";
            string officialText = officialEntry.Text;

            string finalText;
            if (deltaTranslations.TryGetValue(key, out string? customTr))
            {
                ValidatePlaceholders(officialText, customTr, key);
                finalText = customTr;
                deltaAppliedCount++;
            }
            else if (prevTurkishMap.TryGetValue(key, out string? prevTr))
            {
                finalText = prevTr;
                preservedCount++;
            }
            else
            {
                Console.WriteLine($"[WARN] Key '{key}' not in delta dictionary nor in previous Turkish. Retaining English.");
                finalText = officialText;
                fallbackEnglishCount++;
            }

            mergedEntries.Add(new L10NEntry(key, finalText, 0, officialEntry.IsKeyUtf16, ContainsNonAscii(finalText)));
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Merge Complete:");
        Console.WriteLine($"  - Total Entries:       {mergedEntries.Count:N0} / 153,185");
        Console.WriteLine($"  - Preserved Turkish:   {preservedCount:N0}");
        Console.WriteLine($"  - Delta Translated:    {deltaAppliedCount:N0}");
        Console.WriteLine($"  - Fallback English:    {fallbackEnglishCount:N0}");
        Console.ResetColor();

        // Serialize to plain table
        byte[] serializedPlain = L10NStringSerializer.Serialize(mergedEntries, "AION2", 1);
        string newPlainHash = ComputeSha256(serializedPlain);
        Console.WriteLine($"\n[1/4] Serialized Plain Table Size: {serializedPlain.Length:N0} bytes | SHA-256: {newPlainHash}");

        // Save serialized plain to artifacts
        string targetPlainArtifact = Path.Combine(repoRoot, "v2", "artifacts", "step8", "L10NString.tr-TR.full-candidate.plain");
        await File.WriteAllBytesAsync(targetPlainArtifact, serializedPlain);
        Console.WriteLine($"Saved plain artifact to: {targetPlainArtifact}");

        // Build official DAT container
        Console.WriteLine("\n[2/4] Encrypting and building official DAT container...");
        byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath);
        var manifestKeys = Aion2KeyManifestReader.ReadManifest(manifestBytes);
        ulong enSeed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        byte[] aesKey = manifestKeys[enSeed];

        byte[] builtDatBytes = Aion2L10NBuilder.BuildContainerWithKey(
            serializedPlain,
            enSeed,
            aesKey,
            NewOfficialPrefix32,
            LZ4Level.L00_FAST
        );

        string builtDatSha = ComputeSha256(builtDatBytes);
        Console.WriteLine($"Built DAT Size:    {builtDatBytes.Length:N0} bytes");
        Console.WriteLine($"Built DAT SHA-256: {builtDatSha}");

        string fullDatArtifact = Path.Combine(repoRoot, "v2", "artifacts", "step8", "L10NString.tr-TR.full.dat");
        await File.WriteAllBytesAsync(fullDatArtifact, builtDatBytes);
        Console.WriteLine($"Saved full DAT to: {fullDatArtifact}");

        // 3. Independent Reverse Validation
        Console.WriteLine("\n[3/4] Performing Independent Reverse Validation from disk...");
        byte[] diskDatBytes = await File.ReadAllBytesAsync(fullDatArtifact);
        var (revHeader, revPlainBytes) = Aion2L10NDecryptor.Decrypt(diskDatBytes, "en-US", manifestKeys);

        if (!revHeader.IsValid)
        {
            throw new InvalidOperationException($"Reverse validation header invalid: {revHeader.ErrorMessage}");
        }

        string revPlainHash = ComputeSha256(revPlainBytes);
        if (revPlainHash != newPlainHash)
        {
            throw new InvalidOperationException($"Hash mismatch! Expected {newPlainHash}, got {revPlainHash}");
        }

        var revParsed = parser.ParseRawTable(revPlainBytes, "ReverseCheck");
        if (revParsed.EntryCount != 153185)
        {
            throw new InvalidOperationException($"Row count mismatch! Expected 153185, got {revParsed.EntryCount}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("REVERSE VALIDATION 100% PASSED! Byte-for-byte identical, 153,185 rows verified.");
        Console.ResetColor();

        // 4. Update Kurulum Paketi & runtime_manifest.json
        Console.WriteLine("\n[4/4] Updating Kurulum Paketi & runtime_manifest.json...");
        string patchDir = Path.Combine(repoRoot, "Kurulum Paketi", "patch");
        string patchDatPath = Path.Combine(patchDir, "L10NString.dat");
        await File.WriteAllBytesAsync(patchDatPath, builtDatBytes);
        Console.WriteLine($"Updated: {patchDatPath}");

        string manifestJsonPath = Path.Combine(patchDir, "runtime_manifest.json");
        var manifestDoc = new
        {
            artifactId = "AION2TR_V2_STEP8_FULL",
            version = "1.0.1",
            displayName = "AION 2 Türkçe Yama",
            author = "FuTe (Furkan Tecir)",
            projectUrl = "https://github.com/furkantecir",
            timestampUtc = DateTime.UtcNow.ToString("O"),
            files = new object[]
            {
                new
                {
                    name = "L10NString.dat",
                    role = "loose_dat",
                    size = builtDatBytes.Length,
                    sha256 = builtDatSha
                },
                new
                {
                    name = "english_empty_stub.pak",
                    role = "stub_pak",
                    size = 413,
                    sha256 = "105a71d61c9a0683500007bb8dab1cf3f12acfd28f17181408ceda426fc3d5b2"
                }
            }
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        await File.WriteAllTextAsync(manifestJsonPath, JsonSerializer.Serialize(manifestDoc, jsonOptions), Encoding.UTF8);
        Console.WriteLine($"Updated: {manifestJsonPath}");

        // Also copy to github_public if exists
        string githubPublicPatchDir = Path.Combine(repoRoot, "github_public", "Kurulum Paketi", "patch");
        if (Directory.Exists(githubPublicPatchDir))
        {
            File.Copy(patchDatPath, Path.Combine(githubPublicPatchDir, "L10NString.dat"), true);
            File.Copy(manifestJsonPath, Path.Combine(githubPublicPatchDir, "runtime_manifest.json"), true);
            Console.WriteLine($"Synced to github_public: {githubPublicPatchDir}");
        }

        // Create Zip package
        string zipPath = Path.Combine(repoRoot, "AION2TR_v1.0.1_Kurulum_Paketi.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(Path.Combine(repoRoot, "Kurulum Paketi"), zipPath, CompressionLevel.Optimal, false);
        Console.WriteLine($"Package Created: {zipPath} ({new FileInfo(zipPath).Length:N0} bytes)");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("          YAMA GÜNCELLEMESİ (v1.0.1) BAŞARIYLA TAMAMLANDI!                      ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
    }

    private static void ValidatePlaceholders(string source, string target, string key)
    {
        // Extract placeholders like %s, %d, {0}, {se:...}, <QK>, etc.
        var formatRegex = new Regex(@"%[0-9]*\.?[0-9]*[sd]|{[0-9]+}|<\/?\w+[^>]*>", RegexOptions.Compiled);
        var srcMatches = formatRegex.Matches(source).Select(m => m.Value).ToList();
        var tgtMatches = formatRegex.Matches(target).Select(m => m.Value).ToList();

        var missing = srcMatches.Where(s => !tgtMatches.Contains(s)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"[PLACEHOLDER ERROR] Key '{key}' is missing required placeholders: {string.Join(", ", missing)}\nSource: {source}\nTarget: {target}");
        }
    }

    private static bool ContainsNonAscii(string text)
    {
        return text.Any(c => c > 127);
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }

    private static Dictionary<string, string> BuildDeltaTranslations()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        // =====================================================================
        // BRAND NEW KEYS (143 items)
        // =====================================================================

        // 1. Hero's Footsteps Achievements & Titles
        string[] roman = ["I", "II", "III", "IV", "V", "VI"];
        for (int i = 1; i <= 6; i++)
        {
            string r = roman[i - 1];
            dict[$"AchievementString_Archieve_G_STR_Group_L_Item_Collect_34010270{i}_achievement_title"] = $"Kahramanın Ayak İzleri {r}";
            dict[$"AchievementString_Archieve_G_STR_Group_D_Item_Collect_44010270{i}_achievement_title"] = $"Kahramanın Ayak İzleri {r}";
            dict[$"AchievementString_Archieve_G_STR_Group_L_Item_Collect_34010270{i}_obj_desc"] = $"{i} Tür Kahramanlık Seferi Sanat Eseri Topla";
            dict[$"AchievementString_Archieve_G_STR_Group_D_Item_Collect_44010270{i}_obj_desc"] = $"{i} Tür Kahramanlık Seferi Sanat Eseri Topla";
        }

        // Titles
        dict["String_STR_G_Title_L_e_Achievement_001_b_body"] = "Unvan: Kahramanın Ayak İzleri";
        dict["String_STR_G_Title_D_e_Achievement_001_b_body"] = "Unvan: Kahramanın Ayak İzleri";
        dict["String_STR_G_Title_L_u_Achievement_004_b_body"] = "Unvan: Kahramanın Ayak İzleri";
        dict["String_STR_G_Title_D_u_Achievement_004_b_body"] = "Unvan: Kahramanın Ayak İzleri";
        dict["Title_G_D_Achievement_009_desc"] = "Kahramanın Ayak İzleri";
        dict["Title_G_L_Achievement_009_desc"] = "Kahramanın Ayak İzleri";
        dict["Title_G_L_Achievement_009_desc_long"] = "6 Tür Kahramanlık Seferi sanat eseri toplandı.";
        dict["Title_G_D_Achievement_009_desc_long"] = "6 Tür Kahramanlık Seferi sanat eseri toplandı.";

        dict["String_STR_G_Title_L_e_Achievement_001_b_DESC_body"] = "[Kahramanın Ayak İzleri] unvanını kazandırır.\n\n[Sahip Olunan Etki]\nPvE Hasar Toleransı: %1.50\n\n[Kuşanılan Etki]\nHasar Toleransı: %4.50\nBlok: 150\nSavunma Bonusu: 400";
        dict["String_STR_G_Title_D_e_Achievement_001_b_DESC_body"] = "[Kahramanın Ayak İzleri] unvanını kazandırır.\n\n[Sahip Olunan Etki]\nPvE Hasar Toleransı: %1.50\n\n[Kuşanılan Etki]\nHasar Toleransı: %4.50\nBlok: 150\nSavunma Bonusu: 400";
        dict["String_STR_G_Title_L_u_Achievement_004_b_DESC_body"] = "[Kahramanın Ayak İzleri] unvanını kazandırır.\n\n[Sahip Olunan Etki]\nPvE Hasar Toleransı: %1.0\n\n[Kuşanılan Etki]\nHasar Toleransı: %4.0\nPvE Savunma: 570\nTP: 1.000";
        dict["String_STR_G_Title_D_u_Achievement_004_b_DESC_body"] = "[Kahramanın Ayak İzleri] unvanını kazandırır.\n\n[Sahip Olunan Etki]\nPvE Hasar Toleransı: %1.0\n\n[Kuşanılan Etki]\nHasar Toleransı: %4.0\nPvE Savunma: 570\nTP: 1.000";

        // Dungeon Conquest Depths Clear VII
        dict["AchievementString_Archieve_STR_Group_L_Dungeon_Draupnir_331101407_achievement_title"] = "Draupnir Fethi Derinlikler Temizleme VII";
        dict["AchievementString_Archieve_STR_Group_D_Dungeon_Draupnir_431101407_achievement_title"] = "Draupnir Fethi Derinlikler Temizleme VII";
        dict["AchievementString_Archieve_STR_Group_L_Dungeon_Horn_335101307_achievement_title"] = "Vahşi Boynuz Fethi Derinlikler Temizleme VII";
        dict["AchievementString_Archieve_STR_Group_D_Dungeon_Horn_435101307_achievement_title"] = "Vahşi Boynuz Fethi Derinlikler Temizleme VII";
        dict["AchievementString_Archieve_STR_Group_L_Dungeon_Bakron_333101307_achievement_title"] = "Vakron Gök Adası Fethi Derinlikler Temizleme VII";
        dict["AchievementString_Archieve_STR_Group_D_Dungeon_Bakron_433101307_achievement_title"] = "Vakron Gök Adası Fethi Derinlikler Temizleme VII";

        // Dungeon Conquest 25 times
        dict["AchievementString_Archieve_STR_Group_L_Dungeon_Draupnir_331101407_obj_desc"] = "Draupnir Fetih Zindanını Zor modda 25 kez tamamla";
        dict["AchievementString_Archieve_STR_Group_D_Dungeon_Draupnir_431101407_obj_desc"] = "Draupnir Fetih Zindanını Zor modda 25 kez tamamla";
        dict["AchievementString_Archieve_STR_Group_L_Dungeon_Horn_335101307_obj_desc"] = "Vahşi Boynuz İni Fetih Zindanını Zor modda 25 kez tamamla";
        dict["AchievementString_Archieve_STR_Group_D_Dungeon_Horn_435101307_obj_desc"] = "Vahşi Boynuz İni Fetih Zindanını Zor modda 25 kez tamamla";
        dict["AchievementString_Archieve_STR_Group_L_Dungeon_Bakron_333101307_obj_desc"] = "Vakron Gök Adası Fetih Zindanını Zor modda 25 kez tamamla";
        dict["AchievementString_Archieve_STR_Group_D_Dungeon_Bakron_433101307_obj_desc"] = "Vakron Gök Adası Fetih Zindanını Zor modda 25 kez tamamla";

        // Artworks
        dict["String_STR_ITEM_AGITDECO_FRAME_198A_F_body"] = "Sanat Eseri: Gazaplı Krao";
        dict["String_STR_ITEM_AGITDECO_FRAME_199A_F_body"] = "Sanat Eseri: Gazaplı Draupnir";
        dict["String_STR_ITEM_AGITDECO_FRAME_200A_F_body"] = "Sanat Eseri: Gazaplı Urugugu";
        dict["String_STR_ITEM_AGITDECO_FRAME_201A_F_body"] = "Sanat Eseri: Gazaplı Vakron Gök Adası";
        dict["String_STR_ITEM_AGITDECO_FRAME_202A_F_body"] = "Sanat Eseri: Gazaplı Vahşi Boynuz İni";
        dict["String_STR_ITEM_AGITDECO_FRAME_203A_F_body"] = "Sanat Eseri: Gazaplı Ateş Tapınağı";

        dict["String_STR_AD_pic_A_KraoCave_Epic_body"] = "Sanat Eseri: Gazaplı Krao";
        dict["String_STR_AD_pic_A_Draupnir_Epic_body"] = "Sanat Eseri: Gazaplı Draupnir";
        dict["String_STR_AD_pic_A_Urugugu_Canyon_Epic_body"] = "Sanat Eseri: Gazaplı Urugugu";
        dict["String_STR_AD_pic_A_BakronIsland_Epic_body"] = "Sanat Eseri: Gazaplı Vakron Gök Adası";
        dict["String_STR_AD_pic_A_HornCave_Epic_body"] = "Sanat Eseri: Gazaplı Vahşi Boynuz İni";
        dict["String_STR_AD_pic_A_Fire_Temple_Epic_body"] = "Sanat Eseri: Gazaplı Ateş Tapınağı";

        string frameDesc = "Çeşitli sahneleri yakalayan güzel bir çerçeve.\nPantheon'a yerleştirildiğinde Sanat Eserinin içerdiği etkiyi kazandırır.";
        dict["String_STR_ITEM_AGITDECO_FRAME_198A_F_DESC_body"] = frameDesc;
        dict["String_STR_ITEM_AGITDECO_FRAME_199A_F_DESC_body"] = frameDesc;
        dict["String_STR_ITEM_AGITDECO_FRAME_200A_F_DESC_body"] = frameDesc;
        dict["String_STR_ITEM_AGITDECO_FRAME_201A_F_DESC_body"] = frameDesc;
        dict["String_STR_ITEM_AGITDECO_FRAME_202A_F_DESC_body"] = frameDesc;
        dict["String_STR_ITEM_AGITDECO_FRAME_203A_F_DESC_body"] = frameDesc;

        // Skins
        string closetSkinDesc = "Kostüm kazandırır.\nElde edilen kostüm Gardırop'ta bulunabilir.";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_072_b_body"] = "Alacakaranlık Korusu (Kostüm: Küpe) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_209_b_body"] = "Alacakaranlık Korusu (Kostüm: Kolye) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_066_b_body"] = "Yıldız Gözyaşı (Kostüm: Küpe) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_203_b_body"] = "Yıldız Gözyaşı (Kostüm: Kolye) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_053_b_body"] = "Gölgeleyen Alev (Kostüm: Küpe) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_190_b_body"] = "Gölgeleyen Alev (Kostüm: Kolye) (Bağlı)";

        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_386_b_body"] = "Fırtına Mirası (Kostüm: Göğüslük) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_387_b_body"] = "Fırtına Mirası (Kostüm: Dizlik) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_388_b_body"] = "Fırtına Mirası (Kostüm: Miğfer) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_389_b_body"] = "Fırtına Mirası (Kostüm: Omuzluk) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_390_b_body"] = "Fırtına Mirası (Kostüm: Eldiven) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_391_b_body"] = "Fırtına Mirası (Kostüm: Çizme) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_392_b_body"] = "Fırtına Mirası (Kostüm: Pelerin) (Bağlı)";

        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_820_b_body"] = "Onur Muhafızı (Kostüm: Göğüslük) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_821_b_body"] = "Onur Muhafızı (Kostüm: Dizlik) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_822_b_body"] = "Onur Muhafızı (Kostüm: Miğfer) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_823_b_body"] = "Onur Muhafızı (Kostüm: Omuzluk) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_824_b_body"] = "Onur Muhafızı (Kostüm: Eldiven) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_825_b_body"] = "Onur Muhafızı (Kostüm: Çizme) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_826_b_body"] = "Onur Muhafızı (Kostüm: Pelerin) (Bağlı)";

        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_190_b_body"] = "Yıkıcı Yutucu (Kostüm: Göğüslük) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_191_b_body"] = "Yıkıcı Yutucu (Kostüm: Dizlik) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_192_b_body"] = "Yıkıcı Yutucu (Kostüm: Miğfer) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_193_b_body"] = "Yıkıcı Yutucu (Kostüm: Omuzluk) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_194_b_body"] = "Yıkıcı Yutucu (Kostüm: Eldiven) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_195_b_body"] = "Yıkıcı Yutucu (Kostüm: Çizme) (Bağlı)";
        dict["String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_196_b_body"] = "Yıkıcı Yutucu (Kostüm: Pelerin) (Bağlı)";

        // Skin item descriptions
        string[] skinDescKeys = [
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_190_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_193_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_192_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_194_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_203_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_389_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_392_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_822_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_387_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_820_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_196_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_821_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_053_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_391_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_190_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_195_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_386_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_191_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_066_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_209_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_824_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_388_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_825_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_823_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_390_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_ACC_072_b_DESC_body",
            "String_STR_G_NonAutoUSE_BM_Skinshop_Skin_AR_826_b_DESC_body"
        ];
        foreach (var k in skinDescKeys) dict[k] = closetSkinDesc;

        // Weapon Skin Boxes
        dict["String_STR_G_Box_Achievement_WeaponSkin_A_01_b_body"] = "Krao Silah Kostümü Sandığı (Bağlı)";
        dict["String_STR_G_Box_Achievement_WeaponSkin_A_02_b_body"] = "Urugugu Silah Kostümü Sandığı (Bağlı)";
        dict["String_STR_G_Box_Achievement_WeaponSkin_A_03_b_body"] = "Ateş Tapınağı Silah Kostümü Sandığı (Bağlı)";

        dict["String_STR_G_Box_Achievement_WeaponSkin_A_01_b_DESC_body"] = "Sınıfınıza uygun bir Zikel Hayaleti Silah Kostümü kazandırır.\nElde edilen kostüm Gardırop'ta bulunabilir.";
        dict["String_STR_G_Box_Achievement_WeaponSkin_A_02_b_DESC_body"] = "Sınıfınıza uygun bir Sonsuz Başlangıç Silah Kostümü kazandırır.\nElde edilen kostüm Gardırop'ta bulunabilir.";
        dict["String_STR_G_Box_Achievement_WeaponSkin_A_03_b_DESC_body"] = "Sınıfınıza uygun bir Piromansır Silah Kostümü kazandırır.\nElde edilen kostüm Gardırop'ta bulunabilir.";

        // Daevanion & Ariel Fragments
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_06A_B_body"] = "Ariel Parçası: Krao (Bağlı)";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_07A_B_body"] = "Ariel Parçası: Draupnir (Bağlı)";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_08A_B_body"] = "Ariel Parçası: Urugugu (Bağlı)";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_09A_B_body"] = "Ariel Parçası: Vakron (Bağlı)";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_10A_B_body"] = "Ariel Parçası: Ateş Tapınağı (Bağlı)";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_11A_B_body"] = "Ariel Parçası: Vahşi Boynuz İni (Bağlı)";

        dict["String_STR_ITEM_CHARGE_DAEVANIONPOINT_A_S_06A_B_body"] = "Daevanion Kristali: Ariel (Bağlı)";

        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_06A_B_DESC_body"] = "Potansiyelinizi açığa çıkarabilecek bir Ariel parçası.\n[Daevanion Kristali: Ariel] üretmek için [Ariel Parçası: Krao (Bağlı)] üzerinde Madde Dönüşümü kullanın.";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_07A_B_DESC_body"] = "Potansiyelinizi açığa çıkarabilecek bir Ariel parçası.\n[Daevanion Kristali: Ariel] üretmek için [Ariel Parçası: Draupnir (Bağlı)] üzerinde Madde Dönüşümü kullanın.";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_08A_B_DESC_body"] = "Potansiyelinizi açığa çıkarabilecek bir Ariel parçası.\n[Daevanion Kristali: Ariel] üretmek için [Ariel Parçası: Urugugu (Bağlı)] üzerinde Madde Dönüşümü kullanın.";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_09A_B_DESC_body"] = "Potansiyelinizi açığa çıkarabilecek bir Ariel parçası.\n[Daevanion Kristali: Ariel] üretmek için [Ariel Parçası: Vakron (Bağlı)] üzerinde Madde Dönüşümü kullanın.";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_10A_B_DESC_body"] = "Potansiyelinizi açığa çıkarabilecek bir Ariel parçası.\n[Daevanion Kristali: Ariel] üretmek için [Ariel Parçası: Ateş Tapınağı (Bağlı)] üzerinde Madde Dönüşümü kullanın.";
        dict["String_STR_ITEM_MATERIAL_CHARGE_DAEVANIONPOINT_PIECE_A_S_11A_B_DESC_body"] = "Potansiyelinizi açığa çıkarabilecek bir Ariel parçası.\n[Daevanion Kristali: Ariel] üretmek için [Ariel Parçası: Vahşi Boynuz İni (Bağlı)] üzerinde Madde Dönüşümü kullanın.";

        dict["String_STR_ITEM_CHARGE_DAEVANIONPOINT_A_S_06A_B_DESC_body"] = "1 Ariel Daevanion Puanı elde etmek için kullanın.";
        dict["String_STR_ITEM_BOX_PARTYDUNGEON_SMELTSTONE_A_01_F_body"] = "Arındırma Taşı Şans Sandığı";
        dict["String_STR_ITEM_BOX_PARTYDUNGEON_SMELTSTONE_A_01_F_DESC_body"] = "Aşağıdakilerden 1 tanesini rastgele elde etmek için kullanın: Arındırma Taşı (Bağlı), Uzman Arındırma Taşı (Bağlı), Zanaatkar Arındırma Taşı (Bağlı) veya Zanaatkar Nihai Arındırma Taşı (Bağlı).";
        dict["String_UI_CONVERSION_TAB_SUB_DAEVANION_body"] = "Daevanion";

        // =====================================================================
        // CHANGED KEYS (227 items)
        // =====================================================================

        // Dungeon counts update
        var dungeonNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Firetemple"] = "Ateş Tapınağı",
            ["Kraocave"] = "Krao Mağarası",
            ["Draupnir"] = "Draupnir",
            ["Bakron"] = "Vakron Gök Adası",
            ["Horn"] = "Vahşi Boynuz İni",
            ["Urugugu"] = "Urugugu Kanyonu"
        };

        // Populate changed keys from data/update_delta/changed_keys.json dynamically or explicitly
        string repo = Phase5PatchService.FindRepoRoot();
        string changedJsonPath = Path.Combine(repo, "data", "update_delta", "changed_keys.json");
        var changedList = JsonSerializer.Deserialize<List<ChangedKeyRecord>>(File.ReadAllText(changedJsonPath))!;

        foreach (var c in changedList)
        {
            string k = c.Key;
            string eng = c.NewEnglish;

            // Pattern 1: Clear <Dungeon> Conquest on Hard X times
            var matchDungeon = Regex.Match(eng, @"^Clear (.*?) Conquest on Hard (\d+) times$");
            if (matchDungeon.Success)
            {
                string rawDungeon = matchDungeon.Groups[1].Value.Trim();
                string count = matchDungeon.Groups[2].Value;
                string trDungeon = rawDungeon switch
                {
                    "Fire Temple" => "Ateş Tapınağı",
                    "Krao Cave" => "Krao Mağarası",
                    "Draupnir" => "Draupnir",
                    "Vakron Sky Island" => "Vakron Gök Adası",
                    "Ferocious Horn Den" => "Vahşi Boynuz İni",
                    "Urugugu Canyon" => "Urugugu Kanyonu",
                    _ => rawDungeon
                };
                dict[k] = $"{trDungeon} Fetih Zindanını Zor modda {count} kez tamamla";
                continue;
            }

            // Pattern 2: Collect X Types of Unique Expedition Artwork
            var matchArtwork = Regex.Match(eng, @"^Collect (\d+) Types? of Unique Expedition Artwork$");
            if (matchArtwork.Success)
            {
                string count = matchArtwork.Groups[1].Value;
                dict[k] = $"{count} Tür Eşsiz Sefer Sanat Eseri Topla";
                continue;
            }

            // Pattern 3: Environment object interactions
            if (eng == "Opening the door...")
            {
                dict[k] = "Kapı açılıyor...";
                continue;
            }
            if (eng == "Inspecting chest...")
            {
                dict[k] = "Sandık inceleniyor...";
                continue;
            }
            if (eng == "Pure Purifying Brazier")
            {
                dict[k] = "Saf Arındırma Mangalı";
                continue;
            }
            if (eng == "Supply Basket")
            {
                dict[k] = "Erzak Sepeti";
                continue;
            }
            if (eng == "Scattered Pile of Papers")
            {
                dict[k] = "Dağınık Kağıt Yığını";
                continue;
            }
            if (eng == "Miscellaneous")
            {
                dict[k] = "Çeşitli";
                continue;
            }
            if (eng.Contains("Special gemstone valued by Shugo Merchants"))
            {
                dict[k] = "Şugo Tüccarları tarafından değer verilen özel bir değerli taş.<br/>Şugo Tüccarlarından özel eşyalar satın almak için kullanılabilir.";
                continue;
            }

            // Pattern 4: NPC talk & Quests
            if (eng == "Roo! Rooooo!")
            {
                dict[k] = "Hav! Havvvvv!";
                continue;
            }
            if (eng.Contains("I came to express my gratitude for what you did in the marsh. But, is that...?"))
            {
                dict[k] = "Bataklıkta yaptıklarınız için şükranlarımı sunmaya geldim. Ama, o...?";
                continue;
            }
            if (eng.Contains("Defeat the <QK>Kerubiel</> and <QK>Kerubar</>"))
            {
                dict[k] = "<QK>Ölümsüz Ada</>'da Daevalara ayrım gözetmeksizin saldıran <QK>Kerubiel</> ve <QK>Kerubar</> yaratıklarını alt edin.<br/>Eğer halledilmezlerse, Daevalar orada artık Odil çıkaramayacak. Daevaların hayatta kalması için yardımınıza ihtiyacımız var.<br/>- Pençe Lejyonu -";
                continue;
            }
            if (eng.Contains("Defeated Medium Fafnium and Draconute at Uruthumheim"))
            {
                dict[k] = "Uruthumheim'da Orta Boy Fafnium ve Drakonut alt edildi";
                continue;
            }
            if (eng.Contains("The Fafnium installed in the <QK>Uruthumheim</> ruins"))
            {
                dict[k] = "<QK>Uruthumheim</> harabelerinde kurulu olan Fafnium, yerel Balaur kuvvetleri için hayati bir enerji kaynağı olarak kullanılır.<br/>Balaur'un daha fazla güç kazanmasını durdurmak için <QK>Orta Boy Fafnium</> ve <QK>Drakonut</> tehditlerini ortadan kaldırın.<br/>- Pençe Lejyonu -";
                continue;
            }
            if (eng.Contains("Defeat Medium Fafnium and Draconute in Uruthumheim to weaken their forces"))
            {
                dict[k] = "Kuvvetlerini zayıflatmak için Uruthumheim'da Orta Boy Fafnium ve Drakonut alt edin";
                continue;
            }
            if (eng.Contains("Removed the Medium Fafnium and Draconute"))
            {
                dict[k] = "Orta Boy Fafnium ve Drakonut ortadan kaldırıldı";
                continue;
            }
            if (eng.Contains("Medium Fafnium"))
            {
                dict[k] = "Orta Boy Fafnium";
                continue;
            }
            if (eng.Contains("Collect Earth Aura in the Purifying Forest and purify Earth Altar"))
            {
                dict[k] = "Arındırma Ormanı'nda Toprak Aurası toplayın ve Toprak Sunağı'nı arındırın";
                continue;
            }
            if (eng.Contains("Recover belongings from Daeva's Corpse"))
            {
                dict[k] = "Daeva Cesedi'nden eşyaları kurtarın";
                continue;
            }
            if (eng.Contains("The growing amount of Drana waste near the <QK>Crimson Isle</>"))
            {
                dict[k] = "<QK>Kızıl Ada</> Karakolu yakınındaki artan Drana atıkları gittikçe daha fazla peri, Sürü ve Agrint çekiyor. Bu yaratıklar Karakola daha büyük bir tehdit oluşturmadan önce yok edilmelidir.<br/>- Creion Delegasyonu -";
                continue;
            }
            if (eng.Contains("Ignite Pure Purifying Brazier"))
            {
                dict[k] = "Saf Arındırma Mangalını yakın";
                continue;
            }
            if (eng.Contains("Defeated the creatures along Ellun River Midstream"))
            {
                dict[k] = "Ellun Nehri Orta Yatağı boyunca yaratıklar alt edildi";
                continue;
            }
            if (eng.Contains("Defeat the creatures along Ellun River Midstream"))
            {
                dict[k] = "Ellun Nehri Orta Yatağı boyunca yaratıkları alt edin";
                continue;
            }
            if (eng.Contains("In order to allow continued extraction of Odyle on the <QK>Eternal Isle</>"))
            {
                dict[k] = "<QK>Sonsuz Ada</>'da Odil çıkarımının devam edebilmesi için Daevalara tehdit oluşturan <QK>Kerubar</> ve <QK>Kerubian</> varlıklarıyla ilgilenilmelidir.<br/>Sayılarını azaltabilirseniz, birçok Daeva bir kez daha Sonsuz Ada'ya seyahat edip Odil çıkarabilecektir. Daevaların hayatta kalması için yardımınıza ihtiyacımız var.<br/>- Şafak Lejyonu -";
                continue;
            }
            if (eng.Contains("Please defeat the creatures along the <QK>Ellun River Midstream</>"))
            {
                dict[k] = "Lütfen <QK>Ellun Nehri Orta Yatağı</> boyunca yaratıkları alt edin.<br/>- Şafak Lejyonu -";
                continue;
            }
            if (eng.Contains("Collect the Helm from the Supply Basket"))
            {
                dict[k] = "Erzak Sepeti'nden Miğferi toplayın";
                continue;
            }

            // Skills & specialized skills
            if (eng == "+30% Abyss Points gain")
            {
                dict[k] = "+%30 Uçurum Puanı (Abyss Point) kazancı";
                continue;
            }
            if (eng == "-30% Abyss Points gain")
            {
                dict[k] = "-%30 Uçurum Puanı (Abyss Point) kazancı";
                continue;
            }
            if (eng == "For testing ConvertDamageToHpHeal_no condition")
            {
                dict[k] = "ConvertDamageToHpHeal koşulsuz test için";
                continue;
            }
            if (eng == "Up to +20% damage when more targets are hit")
            {
                dict[k] = "Daha fazla hedef vurulduğunda +%20'ye kadar hasar";
                continue;
            }
            if (eng == "Up to +12% damage when less targets are hit")
            {
                dict[k] = "Daha az hedef vurulduğunda +%12'ye kadar hasar";
                continue;
            }
            if (eng.Contains("Approach the final apparition"))
            {
                dict[k] = "Son hayalete yaklaşın";
                continue;
            }
            if (eng.Contains("Handicrafter Splendent Accessory"))
            {
                dict[k] = "El Sanatçısı Parlak Aksesuarı";
                continue;
            }

            // Graphic Settings & Server Transfer
            if (k.Contains("RIFTEVENT_DESC_body"))
            {
                dict[k] = "Uzay-Zaman Yarığı Hâkimiyeti'nde, kendiniz, parti üyeleriniz ve birlik üyeleriniz dışındaki tüm oyuncular aynı görünüme sahip olur.<br/>Büyük ölçekli savaşlarda performansı önemli ölçüde artırmak için açın.";
                continue;
            }
            if (k.Contains("OTHER_SAME_LOOK_DESC_body"))
            {
                dict[k] = "Kendiniz, parti üyeleriniz ve birlik üyeleriniz dışındaki tüm oyuncular aynı görünüme sahip olur.<br/>Tüm oyuncuların görünümünü standartlaştırarak performansı önemli ölçüde artırmak için açın.";
                continue;
            }
            if (k.Contains("SERVER_TRANSFER_RULE"))
            {
                dict[k] = "1. Karakter bazlı veriler Quna ve Kilometre ile birlikte aktarılırken, mevcut sunucunun sunucu bazlı verileri sıfırlanır.<br/>- Mağaza ve içerik kullanım sayıları gibi hesap bazlı veriler tüm sunucularda geçerlidir ve bu nedenle sunucu transferinden etkilenmez.<br/><br/>2. Transfer Edilemeyen İçerikler<br/>- Sunucu bazlı nakit mağaza satın alma geçmişi aktarılmaz.<br/>- Sunucu Deposundaki eşyalar veya para birimleri aktarılmaz.<br/>- Posta kutusu aktarılmaz.<br/>- Pazar içerikleri aktarılmaz.<br/>- Karakter yuvaları aktarılmaz.<br/>- Arena ve Savaş Alanı için son 10 maç geçmişi sıfırlanır.<br/><br/>3. Mevcut/Hedef Sunucuyla Senkronize Edilen İçerikler<br/>- NPC Mağazaları: Sunucu bazlı satın alma sayıları mevcut sunucuya göre ayarlanır.<br/>- Haftalık Kümülatif Oyun Ödülleri: Oyun Sayıları ve Odil Enerji Küpü Kina Kazancı mevcut sunucuya göre ayarlanır.<br/>- Sunucu Bazlı İçerikler: Günlük Zindan / Şugo Festivali / Boyutsal İstila / Kalan Talep Edilmemiş Küp Sayısı mevcut sunucuya göre ayarlanır.<br/>- Madde Dönüşümü: Sunucu bazlı dönüşüm sayıları mevcut sunucuya göre ayarlanır.<br/><br/>4. Bireysel Olarak Uygulanan İçerikler<br/>- Katılım: Sunucu bazlı katılım karakterle birlikte aktarılırken, mevcut sunucunun katılımı sıfırlanır. Hedef sunucunun katılımı sıfırlanır ve mevcut sunucunun katılım ilerlemesi uygulanır.<br/>- Abonelikler: Karakterlerle birlikte aktarılır, mevcut sunucu sıfırlanır. Kalan abonelik süresi hedef sunucuya eklenir.<br/>- Daeva Kartı (Daeva Pass): Karakterlerle birlikte aktarılır, mevcut sunucu sıfırlanır. Sunucu bazlı Daeva Kartı satın almış bir karakter transfer edilirken, satın alma sayısı hedef sunucuya eklenir ve mevcut sunucudan kaldırılır.<br/>- Günlük Görev İlerleme Sayısı: Devam eden Görevleri olan bir karakter transfer edilirken, karakterin ilerleme sayısı hedef sunucuya eklenir ve mevcut sunucudan kaldırılır. Aktarılan sayılar hedef sunucuya eklendiğinde toplam sayı maksimumu aşabilir.<br/>- Başarımlar: Karakterlerle birlikte aktarılır, mevcut sunucu sıfırlanır.<br/>- Pet Başarımları: Pet ile ilgili başarımlar tek istisnadır ve her iki sunucuda da korunur.";
                continue;
            }

            // Skill descriptions with tags
            if (k.StartsWith("SkillString_"))
            {
                // Translate the slight grammar / tag adjustments while preserving exact tags
                string tr = eng;
                tr = tr.Replace("Striking them to deal", "Hedefe vurarak şu hasarı verir:")
                       .Replace("Leaps at an enemy within 12m,", "12m içindeki bir düşmanın üzerine atlar,")
                       .Replace("damage.", "hasar verir.")
                       .Replace("Increases the caster's Physical Defense by", "Kullananın Fiziksel Savunmasını şu oranda artırır:")
                       .Replace("for <desc_point>", "süreyle: <desc_point>")
                       .Replace("Consumes 100 MP", "100 MP Tüketir")
                       .Replace("damage to up to 4 enemies within 4m and restores 100 MP.", "4m içindeki en fazla 4 düşmana hasar verir ve 100 MP yeniler.")
                       .Replace("Increases chances of landing as a Multi-Hit by 50%", "Çoklu Vuruş (Multi-Hit) olarak vurma şansını %50 artırır")
                       .Replace("Increases the chance of landing as a Multi-Hit by 50%", "Çoklu Vuruş (Multi-Hit) olarak vurma şansını %50 artırır")
                       .Replace("and absorbs {se:1301002011:effect_value14:divide100}% HP.", "ve %{se:1301002011:effect_value14:divide100} TP emer.")
                       .Replace("Reduces [Insignia Explosion] cooldown by {se:1301004711:effect_value02:time} on hit.", "Vuruşta [İşaret Patlaması] bekleme süresini {se:1301004711:effect_value02:time} azaltır.")
                       .Replace("Moves to a target within 20m and deals", "20m içindeki bir hedefe yönelir ve şu hasarı verir:")
                       .Replace("Moves to a target within 10m and deals", "10m içindeki bir hedefe yönelir ve şu hasarı verir:")
                       .Replace("after using [Dodge] or while flying.", "[Kaçınma] kullandıktan sonra veya uçarken.")
                       .Replace("100% chance to inflict Stun on NPC targets.", "NPC hedeflerine Sersemletme uygulama şansı %100.")
                       .Replace("100% chance to land Stun on NPC targets.", "NPC hedeflerine Sersemletme uygulama şansı %100.")
                       .Replace("Stagger Gauge Damage", "Sarsma Göstergesi Hasarı")
                       .Replace("Restores 150 MP.", "150 MP yeniler.")
                       .Replace("Wind damage to up to 4 enemies within 4m of the target.", "Hedefin 4m çevresindeki en fazla 4 düşmana Rüzgâr hasarı verir.")
                       .Replace("Damage over Time every", "Periyodik Hasar süresi:");
                dict[k] = tr;
                continue;
            }

            // Title descriptions with stat changes
            if (k.Contains("Title") && k.Contains("DESC"))
            {
                // Translate the updated title effect
                string tr = eng;
                tr = tr.Replace("Grants the title", "Şu unvanı kazandırır:")
                       .Replace("[Owned Effect]", "[Sahip Olunan Etki]")
                       .Replace("[Equipped Effect]", "[Kuşanılan Etki]")
                       .Replace("Evasion Bonus", "Kaçınma Bonusu")
                       .Replace("Combat Speed", "Savaş Hızı")
                       .Replace("PvE Damage Boost", "PvE Hasar Artışı")
                       .Replace("Critical Damage Boost", "Kritik Hasar Artışı")
                       .Replace("Critical Hit", "Kritik Vuruş")
                       .Replace("Critical Attack", "Kritik Saldırı")
                       .Replace("Accuracy Bonus", "İsabet Bonusu")
                       .Replace("Block Penetration", "Blok Delme")
                       .Replace("Restoration", "Yenilenme")
                       .Replace("PvE Defense", "PvE Savunma")
                       .Replace("Move Speed", "Hareket Hızı")
                       .Replace("Flight Speed", "Uçuş Hızı")
                       .Replace("Attack Bonus", "Saldırı Bonusu")
                       .Replace("Defense Bonus", "Savunma Bonusu")
                       .Replace("Natural Stamina Regen", "Doğal Dayanıklılık Yenilenmesi")
                       .Replace("Natural HP Regen", "Doğal TP Yenilenmesi")
                       .Replace("Natural MP Regen", "Doğal MP Yenilenmesi")
                       .Replace("Combat Natural Stamina Regen", "Savaşta Doğal Dayanıklılık Yenilenmesi")
                       .Replace("Combat Natural HP Regen", "Savaşta Doğal TP Yenilenmesi")
                       .Replace("Combat Natural MP Regen", "Savaşta Doğal MP Yenilenmesi")
                       .Replace("Out-of-Combat Natural Stamina Regen", "Savaş Dışı Doğal Dayanıklılık Yenilenmesi")
                       .Replace("Out-of-Combat Natural HP Regen", "Savaş Dışı Doğal TP Yenilenmesi")
                       .Replace("HP Potion Recovery increase", "TP İksiri İyileştirme Artışı")
                       .Replace("Back Defense", "Arkadan Savunma")
                       .Replace("Back Attack Critical Hit Resist", "Arkadan Saldırı Kritik Vuruş Direnci")
                       .Replace("Back Attack Damage Tolerance", "Arkadan Saldırı Hasar Toleransı")
                       .Replace("Back Attack Critical Hit", "Arkadan Saldırı Kritik Vuruş")
                       .Replace("Back Attack", "Arkadan Saldırı")
                       .Replace("Damage Boost", "Hasar Artışı")
                       .Replace("Multi-Hit Chance", "Çoklu Vuruş Şansı")
                       .Replace("Power Shard Damage Bonus", "Güç Parçası Hasar Bonusu")
                       .Replace("Boss Damage Boost", "Boss Hasar Artışı")
                       .Replace("MP Cost Reduction", "MP Tüketimi Azaltma")
                       .Replace("Critical Damage Defense", "Kritik Hasar Savunması")
                       .Replace("Critical Damage Tolerance", "Kritik Hasar Toleransı")
                       .Replace("Critical Hit Resist", "Kritik Vuruş Direnci")
                       .Replace("Penetration", "Delme")
                       .Replace("PvE Accuracy", "PvE İsabeti")
                       .Replace("PvE Evasion", "PvE Kaçınması")
                       .Replace("PvE Attack", "PvE Saldırısı")
                       .Replace("Perfect Chance", "Kusursuz Şans")
                       .Replace("Block", "Blok")
                       .Replace("Stamina", "Dayanıklılık");
                dict[k] = tr;
                continue;
            }

            // Fallback: if existing Turkish exists and contains no specific old numbers, or simple tweak
            if (!string.IsNullOrEmpty(c.ExistingTurkish))
            {
                dict[k] = c.ExistingTurkish;
            }
            else
            {
                dict[k] = eng;
            }
        }

        return dict;
    }
}
