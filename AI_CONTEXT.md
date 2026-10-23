# Aion2TR — Proje Özeti, Dizin Haritası ve Gelecek Güncelleme Rehberi (AI Context)

Bu doküman, projeye gelecekte dönüldüğünde yapay zekanın (veya geliştiricinin) tüm projeyi sıfırdan taramasına gerek kalmadan; **ne yapıldığını**, **hangi dosyanın nerede olduğunu** ve **oyuna güncelleme geldiğinde adım adım ne yapılacağını** saniyeler içinde anlaması için hazırlanmış ana rehberdir.

---

## 1. Proje Kimliği ve Temel Bilgiler

- **Proje Adı:** Aion2TR (AION 2 Türkçe Yerelleştirme ve Başlatıcı)
- **Geliştirici:** Furkan Tecir (FuTe)
- **Oyun:** AION 2 (Unreal Engine 5.3)
- **Steam AppID:** `3393110`
- **Mevcut Sürüm:** v1.0.1 (Portable / Taşınabilir - 2026-10-23 Güncellemesi)
- **Depolar:**
  - **Gizli (Private) Tam Yedek:** `https://github.com/furkantecir/A-ON2_TURK-E_YAMA.git`
  - **Genel (Public) Açık Kaynak:** `https://github.com/furkantecir/AION2_TURKCE_YAMA.git`
- **Lisans:** MIT Lisansı

---

## 2. Bugüne Kadar Ne Yapıldı? (Aşama Özeti)

1. **Tersine Mühendislik ve Güvenlik:**
   - Eski şüpheli yama yapısı ve arka kapı (backdoor) riskleri incelendi, tamamen elendi.
   - Oyunun ana çalıştırılabilir dosyalarına (`.exe`), DLL'lerine veya oyun belleğine müdahale etmeyen; yalnızca `pakchunk502000-Windows_0_P.pak` arşivindeki `L10NString.dat` veri dosyasını işleyen sıfır riskli bir altyapı kuruldu.
2. **Çeviri Sözlüğü ve Doğrulama:**
   - 153.185 satırlık dil veritabanı Unreal Engine format değişkenleri (`%s`, `{0}`, `<color>` vb.) korunarak eksiksiz Türkçeleştirildi.
   - 2026-10-23 oyun güncellemesinde 152.815 satır sıfır kayıpla korunmuş, 370 adet yeni/güncellenen satır tercüme edilerek v1.0.1 yaması derlenmiştir.
   - Oyunda canlı olarak test edildi ve sorunsuz çalıştığı doğrulandı.
3. **WPF Modern Başlatıcı (Launcher):**
   - MVVM mimarisiyle modern, karanlık temalı masaüstü arayüzü kodlandı.
   - 1.2 saniyelik altın renkli FuTe marka açılış animasyonu eklendi.
   - Steam Registry ve `libraryfolders.vdf` üzerinden otomatik oyun dizini algılama entegre edildi.
   - Tek tıkla "Yamayı Kur", "Orijinale Dön (Yamayı Kaldır)" ve "Oyunu Başlat" mekanizmaları tamamlandı.
4. **Dağıtım Paketi:**
   - Kurulum sihirbazı gerektirmeyen, bağımsız taşınabilir `Kurulum Paketi/` ve `AION2TR_v1.0.1_Kurulum_Paketi.zip` oluşturuldu.
   - 78 adet birim ve entegrasyon testi (%100 başarıyla) tamamlandı.
5. **Açık Kaynak Dağıtımı:**
   - İç geliştirme raporlarından arındırılmış `github_public` klasörü hazırlanıp GitHub'a yüklendi ve v1.0.1 Release paketi senkronize edildi.

---

## 3. Kapsamlı Dizin ve Dosya Haritası (Hangi Dosya Nerede?)

```text
AİON2_TURKCE_YAMA/
│
├── AI_CONTEXT.md                           # <<< BU DOKÜMAN (Gelecek oturumlar için ana harita)
├── README.md                               # Proje genel tanıtım dokümanı
├── Aion2TR.sln                             # Visual Studio ana çözüm dosyası (.NET 10 / net10.0-windows)
├── .gitignore                              # Git hariç tutma kuralları (büyük dosyalar, .exe, .pak)
│
├── Kurulum Paketi/                         # Son kullanıcıya giden taşınabilir dağıtım klasörü
│   ├── Aion2TR.exe                         # WPF Tek Dosya Başlatıcı (Self-contained)
│   ├── BENI OKU - KURULUM REHBERI.txt      # Kullanıcı talimatları
│   └── patch/                              # Dağıtılacak yama veri paketi
│       ├── L10NString.dat                  # 153.185 satırlık Türkçe ikili veri dosyası
│       ├── english_empty_stub.pak          # Güvenli dil paketi stub dosyası
│       └── runtime_manifest.json           # SHA-256 bütünlük ve sürüm bildirimi (v1.0.1)
│
├── src/                                    # Ana Uygulama Kaynak Kodları
│   ├── Aion2TR.App/                        # WPF Kullanıcı Arayüzü (XAML + C#)
│   │   ├── MainWindow.xaml                 # Modern Stitch tasarımına sahip ana pencere
│   │   ├── MainWindow.xaml.cs              # 1.2s FuTe açılış animasyonu ve pencere kontrolleri
│   │   ├── ViewModels/MainViewModel.cs     # Kurulum, geri alma, oyun başlatma durum yönetimi
│   │   ├── Assets/                         # Logo, ikon, arka plan ve FuTe monogram görselleri
│   │   └── app.manifest                    # asInvoker güvenlik bildirim dosyası
│   ├── Aion2TR.Core/                       # İş modelleri, hash servisleri, arayüzler
│   └── Aion2TR.Infrastructure/             # Steam keşif servisi, dosya okuyucular
│
├── v2/                                     # Çeviri Motoru, Araçlar ve Testler
│   ├── src/
│   │   ├── CleanRuntimeInstaller/          # Atomik kurulum, geri alma ve yedekleme motoru (ÖNEMLİ)
│   │   │   ├── RuntimeInstallerEngine.cs   # Dosya kopyalama, yedek alma ve SHA-256 doğrulama
│   │   │   └── ProcessDetector.cs          # Aion2.exe çalışıyor mu denetleyicisi
│   │   ├── CleanDatBuilder/                # L10NString.dat oluşturan ikili derleme motoru
│   │   ├── CleanMigrationEngine/           # Eski çeviriler ile yeni metinleri eşleyen motor
│   │   ├── CleanRemainingTranslator/       # Eksik çevirileri tamamlayan motor
│   │   └── CleanSettingsTranslator/        # Oyun içi ayarlar çeviri modülü
│   ├── data/
│   │   ├── legacy_to_current_match.json    # 153.042 satırlık ana eşleme veritabanı
│   │   └── unmapped_template.txt           # Çevrilmemiş şablonlar
│   └── tests/
│       └── CleanRuntimeInstaller.Tests/    # 28 adet başarılı çalışma zamanı testi
│
├── screenshots/                            # README için çerçeveli ekran görüntüleri
│   ├── intro_animation.png                 # FuTe açılış animasyonu ekranı
│   └── launcher_main.png                   # Ana başlatıcı arayüz ekranı
│
├── docs/                                   # Mimari, format ve güvenlik dokümantasyonları
│   ├── ARCHITECTURE.md                     # Sistem mimarisi
│   ├── L10N_FORMAT.md                      # Unreal L10N ikili dosya yapısı
│   ├── PATCH_STRATEGY.md                   # Yama stratejisi
│   └── SAFETY.md                           # Güvenlik ilkeleri
│
├── github_public/                          # Public (açık kaynak) deponun temiz klon klasörü
└── AION2TR_v1.0.1_Kurulum_Paketi.zip       # Dağıtılmaya hazır paket arşivi (69.7 MB)
```

---

## 4. Oyuna Güncelleme Geldiğinde Yapılacak Adım Adım Protokol

Kullanıcı "Oyuna güncelleme geldi, yeni metinleri çevirip yamayı güncelleyelim" dediğinde **tüm dosyaları baştan taramayın**. Aşağıdaki 6 adımı sırasıyla uygulayın:

### Adım 1: Yeni Oyun Dosyasını Alın
- Steam oyun dizinindeki güncellenmiş PAK dosyasını kontrol edin:
  `D:\uygulamalar\steam\steamapps\common\AION2\Aion2\Content\Paks\pakchunk502000-Windows_0_P.pak`
- İçerisindeki güncel `L10NString.dat` dosyasını çıkartın.

### Adım 2: Fark Analizi Yapın (Diff / Migration)
- `v2/src/CleanMigrationEngine` aracını çalıştırın.
- Güncel İngilizce metinleri mevcut `v2/data/legacy_to_current_match.json` (153.042 satır) ile karşılaştırın.
- **Kural:** Mevcut çevrilmiş 153.042 satıra ASLA dokunmayın. Sadece yeni eklenen veya değişen satırları (Fark / Delta) ayıklayın.

### Adım 3: Sadece Yeni Satırları Çevirin
- Ayıklanan yeni satırları `v2/src/CleanRemainingTranslator` ile çevirin.
- Çevrilen yeni satırları mevcut sözlüğe ekleyin.

### Adım 4: Yeni L10NString.dat Dosyasını Derleyin
- `v2/src/CleanDatBuilder` aracını çalıştırarak güncel ikili `L10NString.dat` dosyasını üretin.
- Üretilen dosyayı `Kurulum Paketi/patch/L10NString.dat` konumuna kopyalayın.
- `Kurulum Paketi/patch/runtime_manifest.json` içindeki SHA-256 hash değerini ve sürüm numarasını (örn: `v1.0.1`) güncelleyin.

### Adım 5: Yeni Kurulum Paketini Sıkıştırın
- `Kurulum Paketi` klasörünü `AION2TR_v1.0.1_Kurulum_Paketi.zip` olarak paketleyin.

### Adım 6: Git ve GitHub'ı Güncelleyin
- `github_public` içerisindeki patch dosyalarını ve sürüm numarasını güncelleyin.
- Değişiklikleri hem özel depoya (`A-ON2_TURK-E_YAMA`) hem de genel depoya (`AION2_TURKCE_YAMA`) push edin.
- Kullanıcıya GitHub Releases üzerinden yeni sürümü yayınlamasını söyleyin.

---

## 5. Kritik Güvenlik ve Kodlama Kuralları (Asla Çiğnenmeyecek İlkeler)

1. **Bellek / Enjeksiyon Yasağı:** Asla `Aion2.exe` bellek alanına DLL inject etmeyin veya bellek yaması yazmayın. Easy Anti-Cheat bunu anında hile sayar. Yalnızca veri katmanında (`L10NString.dat`) çalışın.
2. **Format Belirteçlerini Koruyun:** Çeviri yaparken `%s`, `%d`, `{0}`, `{PlayerName}`, `<color>` gibi motor değişkenlerini asla değiştirmeyin veya silmeyin.
3. **GitHub Boyut Sınırı (100 MB):** GitHub 100 MB üzeri tekil dosyaları kesinlikle reddeder. `.exe`, `.pak`, ham dump veya büyük SQLite veritabanlarını git'e eklemeyin (`.gitignore` korumasını bozmayın).
4. **Taşınabilirlik (Portable):** Kurulum sihirbazı veya Inno Setup kullanmayın; tek dosya `Aion2TR.exe` + `patch/` taşınabilir yapısını koruyun.
