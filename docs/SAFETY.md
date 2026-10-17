# Aion2TR — Güvenlik Sınırları ve İlkeleri (SAFETY.md)

Bu araç, **yalnızca kişisel yerelleştirme ve modlama amacıyla** geliştirilmektedir. Kullanıcı hesabının, oyun kurulumunun ve sistemin güvenliği projenin en yüksek önceliğidir.

---

## 1. Kesin Güvenlik Sınırları

Aion2TR:
- ❌ **Kullanıcı kimlik bilgisi (credential/token) toplamaz ve saklamaz.**
- ❌ **Hiçbir dış sunucuya telemetri, izleme veya analitik verisi göndermez.**
- ❌ **Anti-cheat (NCGuard / EAC) bypass yapmaz veya güvenlik mekanizmalarını devre dışı bırakmaya çalışmaz.**
- ❌ **Process injection (DLL injection, code injection) yapmaz.**
- ❌ **Oyun belleğine (RAM) veya çalışan process memory'sine müdahale etmez.**
- ❌ **Ağ trafiğini (network packet sniffing / packet editing) manipüle etmez.**
- ❌ **Oyunun çalıştırılabilir dosyalarını (.exe / .dll) patchlemez veya değiştirmez.**
- ❌ **Bilinmeyen harici binary (.exe) indirmez veya çalıştırmaz.**

---

## 2. Dosya Sistemi ve Veri Bütünlüğü Güvenliği

1. **İlk Çalıştırmada Salt-Okunur (Read-Only) İlkesi:**
   - Oyun dosyaları üzerinde analiz yapılırken dosyalar sadece okunur; asla değiştirilmez.
2. **Yedek Doğrulanmadan Dokunulmazlık:**
   - Herhangi bir yama dosyası uygulanmadan önce tam yedek alınır ve SHA-256 bütünlüğü onaylanır.
3. **Dry-Run (Simülasyon Modu):**
   - Kullanıcı dilediğinde hiçbir dosyayı değiştirmeden yama planını ve risk analizini görebilir.
4. **Git Güvenliği:**
   - Oyunun telifli binary dosyaları, şifreli arşivleri, yedek klasörleri ve API anahtarları `.gitignore` ile korunur ve repoya commit edilemez.
