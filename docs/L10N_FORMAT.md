# AION 2 — L10NString.dat ve Paket Formatı Dokümanı (L10N_FORMAT.md)

Bu doküman, AION 2'nin yerelleştirme veri yapısına ilişkin tersine mühendislik ve doğrulanmış binary analiz bulgularını özetler.

Bilgiler kanıt düzeyine göre **CONFIRMED** (Doğrulanmış), **DISPROVEN** (Çürütülmüş) ve **UNKNOWN** (Araştırılıyor) olarak sınıflandırılmıştır.

---

## 1. Unreal Engine Paketleme Katmanı (CONFIRMED)

| Parametre | Durum | Değer / Kanıt |
| :--- | :--- | :--- |
| **Oyun Motoru Sürümü** | **CONFIRMED** | Unreal Engine 5.3 |
| **PAK Dosya Formatı** | **CONFIRMED** | Version 11 (UE 4.26 - UE 5.3 Pak Standardı) |
| **Pak Sihirli Sayısı (Magic)** | **CONFIRMED** | `0x5A6F12E1` (Little Endian: `E1 12 6F 5A`) |
| **Pak AES Anahtarı** | **CONFIRMED** | `0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF` |
| **İngilizce Metin Konumu** | **CONFIRMED** | `Aion2/Content/Paks/L10N/Text/en-US/pakchunk502000-Windows_0_P.pak` |
| **Pak İçi Dosya Yolu** | **CONFIRMED** | `Aion2/Content/L10N/Text/en-US/L10NString.dat` |
| **Startup Metin Konumu** | **CONFIRMED** | `Aion2/Content/StartUpData/Table/L10N/en-US/L10NString.dat` |
| **Key Manifest Konumu** | **CONFIRMED** | `Aion2/Content/StartUpData/Table/key_manifest.dat` |

---

## 2. İki Katmanlı Yapı: Container Format vs. Raw L10N Table Format

AION 2 yerelleştirme verisi iki ayrı katmandan oluşmaktadır:
1. **Container Format (Şifreli ve Sıkıştırılmış Kapsayıcı)**
2. **Raw L10N Table Format (Açılmış Düz Metin / İkili Tablo)**

---

## 3. Katman 1: Container Format (CONFIRMED)

`L10NString.dat` dosyası diskte veya PAK içinde şifreli bir container olarak saklanır.

### A. Container Header Yapısı
| Offset | Boyut | Tip | Alan | Açıklama |
|---|---|---|---|---|
| `0x00` | 4 bayt | `int32` | `Version` | Sabit `2` (Tüm dillerde) |
| `0x04` | 16 bayt | `byte[16]` | `EncryptedHeaderBlock` | XOR ile şifrelenmiş başlık meta verisi |
| `0x14` | Değişken | `byte[AlignedSize]` | `EncryptedPayload` | AES-256-ECB ile şifrelenmiş veri gövdesi |

### B. Header Şifre Çözümü (Header Key Derivation)
Encrypted header bloğu (16 bayt), BLAKE3 tabanlı türetilmiş bir anahtarla XOR işlemine tabi tutulur:
1. **Header Key Root:** `Blake3Hash(0xCD02190910CE83F6UL)`
2. **Seed:** `Blake3Hash("L10NString_" + locale)` (Örnek `en-US` için `0xA875363B0593FEA3`)
3. **Key Derivation Buffer (24 bayt):**
   - Bayt 0..7: `HeaderKeyRoot` (uint64 little-endian)
   - Bayt 8..15: `Seed` (uint64 little-endian)
   - Bayt 16..19: `EHeaderType.Localization = 3` (int32 little-endian)
   - Bayt 20..23: `0x00, 0x00, 0x00, 0x00` (padding)
4. **Header Key:** `Blake3Hash(KeyDerivationBuffer)` (32 bayt)
5. `headerBlock[i] ^= headerKey[i % 32]` uygulanır.

### C. Açılan Header Alanları (CONFIRMED)
XOR işlemi sonrasında elde edilen 16 bayt:
- `PackedSize` (`int32`, `0x00`): LZ4 sıkıştırılmış akış boyutu (`3,745,400` bayt).
- `EncryptionType` (`int32`, `0x04`): Şifreleme türü (`2 = CompressedAES`).
- `AlignedSize` (`int32`, `0x08`): 16 bayt blok hizalı şifreli payload boyutu (`3,745,440` bayt).
- `RawSize` (`int32`, `0x0C`): LZ4 açıldıktan sonraki tam ham boyut (`17,429,693` bayt).

### D. Payload Deşifresi ve Sıkıştırma Açma (CONFIRMED)
1. **AES Anahtarı:** `key_manifest.dat` içinden `Seed = 0xA875363B0593FEA3` aranarak bulunur:
   - `0CFDAB7BCB727E404A20577C83BE3399207F02AB68C18F300D36FD3C66CB218E`
2. **AES Modu:** AES-256-ECB, PaddingMode.None.
3. **Payload Aralığı:** Offset `0x14`, uzunluk `AlignedSize` (`3,745,440` bayt).
4. **LZ4 Decompression:** Deşifre edilen buffer'ın **`0x20`** (32.) baytından başlanarak `PackedSize` (`3,745,400` bayt) okunur.
5. **Çıktı:** Tam `RawSize` (`17,429,693` bayt) ham tablo verisi elde edilir.

---

## 4. Katman 2: Raw L10N Table Format (CONFIRMED)

Decompress edilen `17,429,693` baytlık ham veri standart bir Unreal Engine `TMap<FString, FString>` serialization yapısıdır:

| Alan | Boyut | Tip | Değer |
|---|---|---|---|
| `TableVersion` | 4 bayt | `int32` | Sabit `1` |
| `Namespace` | Değişken | `FString` | `"AION2"` (length = 6, bytes: `41 49 4F 4E 32 00`) |
| `EntryCount` | 4 bayt | `int32` | `153,042` kayıt |
| `Entries` | Değişken | `Array` | 153,042 adet `(FString Key, FString Text)` çifti |

### FString Yapısı
- `int32 length`:
  - `length > 0`: UTF-8 string (`length - 1` karakter + `\0` null terminator).
  - `length < 0`: UTF-16LE string (`-length - 1` karakter + `\0\0` null terminator).
  - `length == 0`: Boş string.

---

## 5. Çürütülen Hipotezler (DISPROVEN)

### 25 A8 7E 91 XOR Hipotezi (DISPROVEN)
- Phase 3'te `L10NString.dat` container verisi üzerine doğrudan `25 A8 7E 91` XOR uygulanmasının veriyi açtığı düşünülmüştü.
- **Çürütme Kanıtı:** Container seviyesinde bu dizi hiçbir anlamlı metin üretmemiştir (çıktı saf rastgele gürültü / 8.0 entropi kalmıştır).
- **Gerçek Rolü:** Bu XOR dizisi (`25 A8 7E 91` ve genişletilmiş `25 00 A8 00 7E 00 91 00`), eski/v1 şifresiz DAT formatlarında string bazlı obfuscation amacıyla kullanılmıştır; v2 container şifrelemesiyle ilgisi yoktur.

---

## 6. Doğrulanmış Ölçüm ve Hash Özeti

| Dosya / Katman | Boyut (Bayt) | SHA-256 | Durum |
|---|---|---|---|
| `key_manifest.dat` | 36,104 | `d44e94bec85f16d41089cd104f0143cb74ef86eeb5e141d3c180fdab29616963` | **CONFIRMED** (752 anahtar) |
| `L10NString.dat` (Container) | 3,745,460 | `694a7d39e6bbe27c85dafa127d3feaca2a4a7a3c502ccf7385078b9c74dc6a3f` | **CONFIRMED** |
| `raw_l10n_main.bin` (RAW) | 17,429,693 | `098b3b7099cd8bf24fc302a65ea32e2fff0f3276142a7dcf54da73c67f85bb7c` | **CONFIRMED** (153,042 kayıt) |
