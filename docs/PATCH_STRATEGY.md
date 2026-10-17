# Aion2TR — Güvenli Yama ve Kurulum Stratejisi (PATCH_STRATEGY.md)

Bu doküman, Aion2TR'nin oyun dosyalarına zarar vermeden, geri alınabilir ve oyun güncellemelerine dayanıklı bir şekilde yerelleştirme uygulama stratejisini detaylandırır.

---

## 1. Temel Prensipler

1. **Orijinal Dosyalara Asla Körlemesine Dokunma:**
   - 90+ GB'lık ana `.ucas` veya `.utoc` IoStore dosyaları kesinlikle değiştirilmez veya üzerine yazılmaz.
   - Orijinal `.sig` (imza) dosyaları rastgele tahrif edilmez.
2. **Anti-Cheat ve Memory Dokunulmazlığı:**
   - NCGuard veya EasyAntiCheat'e müdahale edilmez.
   - DLL injection, process hooking veya runtime memory patch uygulanmaz.
   - Yama yalnızca Unreal Engine'in desteklediği dosya seviyesinde (data override) çalışır.

---

## 2. Unreal Engine Patch / Override Katmanı

Unreal Engine pak yükleme mekanizmasında:
- Aynı chunk numarasına sahip ve dosya adı son eki `_P` (Patch) olan paketler veya alfabetik olarak sonra gelen paketler, önceki paketlerdeki aynı dosya yollarını (mount path) ezer (override).
- **Hedef:** `pakchunk502000-Windows_0_P.pak` (İngilizce metin paketi) yerel olarak modifiye edilmek istendiğinde:
  - **Strateji A (Yama Paketi):** İzole bir override paketi oluşturmak.
  - **Strateji B (Atomik Yer Değiştirme):** Orijinal `pakchunk502000-Windows_0_P.pak` dosyasının yedeğini alıp atomik olarak Türkçe metinleri içeren doğrulanmış yeni paketi yerleştirmek.

---

## 3. Atomik Dosya İşlemleri (Atomic File Operations)

Bir dosya doğrudan hedef konuma yazılmaz:
1. `pakchunk502000-Windows_0_P.pak.tmp` adıyla geçici dosya yazılır.
2. Geçici dosyanın SHA-256 hash'i alınır ve pak parser ile bütünlüğü doğrulanır.
3. Doğrulama başarılıysa `File.Move(..., overwrite: true)` veya Win32 `ReplaceFile` API'si ile atomik olarak devreye alınır.
4. Herhangi bir çökmede `transaction.json` durumu takip edilir ve yarım kalan patch varsa otomatik rollback önerilir.

---

## 4. Yedekleme ve Geri Alma (Backup & Restore)

- Yama kurulmadan önce hedef dosya `backup/<timestamp>/` altına kopyalanır.
- `manifest.json` içine orijinal dosyanın göreli yolu, boyutu ve SHA-256 hash'i kaydedilir.
- "Yamayı Kaldır" dendiğinde dosya geri kopyalanır ve SHA-256 hash'i doğrulanarak orijinal duruma dönüldüğü teyit edilir.

---

## 5. Güncelleme Algılama (Game Update Fingerprint)

`GameFingerprintService`:
- `pakchunk502000-Windows_0_P.pak` ve `Manifest_UFSFiles_Win64.txt` dosyalarının hash/size/mtime bilgilerini `state/game_state.json` içinde tutar.
- Oyun Steam'den güncellendiğinde hash değişimi algılanır:
  `⚠ AION 2 güncellenmiş görünüyor.` uyarısı verilir.
- Eski yama körlemesine uygulanmaz; çeviri veritabanındaki (SQLite) mevcut çeviriler yeni güncellenen string tablolarıyla anahtar/hash üzerinden otomatik eşleştirilir (UpdateDiffService).
