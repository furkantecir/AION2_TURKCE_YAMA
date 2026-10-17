using System;
using System.IO;
using System.Linq;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace Aion2TR.Phase3Analyzer;

internal class CompareLanguages
{
    private const string Aion2AesKeyHex = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";

    public static void Run(string gamePath)
    {
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("          TÜM DİLLERDEKİ L10NString.dat BAŞLIKLARININ KARŞILAŞTIRILMASI          ");
        Console.WriteLine("================================================================================");

        string[] langs = ["en-US", "ko-KR", "ja-JP", "de-DE", "fr-FR", "es-ES", "pt-BR", "ru-RU"];

        foreach (var lang in langs)
        {
            var pakDir = Path.Combine(gamePath, "Aion2", "Content", "Paks", "L10N", "Text", lang);
            if (!Directory.Exists(pakDir)) continue;

            try
            {
                var provider = new DefaultFileProvider(new DirectoryInfo(pakDir), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
                provider.Initialize();
                var aesKey = new FAesKey(Aion2AesKeyHex);
                if (provider.UnloadedVfs.Count > 0)
                {
                    provider.SubmitKey(provider.UnloadedVfs.First().EncryptionKeyGuid, aesKey);
                }

                var internalPath = $"Aion2/Content/L10N/Text/{lang}/L10NString.dat";
                if (provider.Files.TryGetValue(internalPath, out var gf))
                {
                    var data = provider.SaveAsset(gf.Path);
                    Console.WriteLine($"[{lang,-5}] Size: {data.Length,9:N0} bytes | First 32 bytes:");
                    Console.WriteLine($"         {BitConverter.ToString(data, 0, 32)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{lang,-5}] Error: {ex.Message}");
            }
        }
    }
}
