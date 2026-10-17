# Aion2TR — AION 2 Türkçe Yerelleştirme ve Yama Yöneticisi

Aion2TR, AION 2 için geliştirilmiş modern, güvenli, taşınabilir ve oyun güncellemelerine tam uyumlu bir Türkçe yerelleştirme yönetim aracıdır.

Geliştirici: **Furkan Tecir (FuTe)**  
GitHub: [https://github.com/furkantecir](https://github.com/furkantecir)  
Proje Bağlantısı: [https://github.com/furkantecir/AION2_TURKCE_YAMA](https://github.com/furkantecir/AION2_TURKCE_YAMA)  
Sürüm: **v1.0.0 (Portable)**  
Lisans: **MIT**  

---

## Uygulama Görselleri

<div align="center">
  <table>
    <tr>
      <th align="center">Açılış Ekranı (Intro & Marka Kimliği)</th>
    </tr>
    <tr>
      <td align="center">
        <br />
        <img src="screenshots/intro_animation.png" alt="Aion 2 Türkçe Yama Açılış Ekranı" width="760" style="border: 1px solid #30363d; border-radius: 8px;" />
        <br /><br />
      </td>
    </tr>
    <tr>
      <th align="center">Ana Yönetim Paneli ve Başlatıcı Arayüzü</th>
    </tr>
    <tr>
      <td align="center">
        <br />
        <img src="screenshots/launcher_main.png" alt="Aion 2 Türkçe Yama Ana Arayüz" width="760" style="border: 1px solid #30363d; border-radius: 8px;" />
        <br /><br />
      </td>
    </tr>
  </table>
</div>

---

## Hızlı Başlangıç (Kullanıcı Rehberi)

1. **İndirme:** Güncel hazır paketi [Releases (Sürümler)](https://github.com/furkantecir/AION2_TURKCE_YAMA/releases) sayfasından `AION2TR_v1.0.0_Kurulum_Paketi.zip` olarak indirin.
2. **Arşivden Çıkarma:** İndirdiğiniz zip arşivini bilgisayarınızda istediğiniz bir klasöre çıkartın.
3. **Çalıştırma:** Klasör içerisindeki `Aion2TR.exe` dosyasını çalıştırın (Herhangi bir kurulum sihirbazı veya yükleme gerektirmez).
4. **Oyun Dizinini Doğrulama:** Başlatıcı Steam kütüphanenizi ve AION 2 dizinini otomatik olarak tanır. Oyun farklı bir konumdaysa "Gözat" ile ana dizini seçebilirsiniz.
5. **Yamayı Kur:** "Yamayı Kur" butonuna tıklayın. Sistem orijinal dosyayı otomatik olarak yedekler ve yerelleştirilmiş veriyi entegre eder.
6. **Oyuna Giriş:** "Oyunu Başlat" butonuyla veya doğrudan Steam üzerinden oyuna giriş yapın.
7. **Orijinale Dönme:** İstediğiniz zaman arayüzdeki "Orijinale Dön (Yamayı Kaldır)" butonuna tıklayarak oyunu tek tıkla orijinal durumuna döndürebilirsiniz.

---

## Öne Çıkan Özellikler

- **Taşınabilir (Portable) Yapı:** Windows Registry (Kayıt Defteri) kirliliği yaratmaz; tamamen bağımsız klasör mimarisiyle çalışır.
- **Otomatik Steam Entegrasyonu:** Steam'in `libraryfolders.vdf` yapılandırmasını okuyarak oyun dizinini otomatik keşfeder; oyun durumunu (çalışıyor/kapalı) anlık izler.
- **Doğrudan Unreal Engine PAK Entegrasyonu:** Yama işlemi harici bellek müdahaleleri yerine doğrudan oyunun standart `pakchunk502000-Windows_0_P.pak` arşiv protokolüyle gerçekleştirilir.
- **153.000+ Satır Kapsamlı Çeviri:** Oyun içi diyaloglar, görev açıklamaları, eşya adları, arayüz metinleri ve sistem terimleri titizlikle yerelleştirilmiştir.
- **SHA-256 Otomatik Yedekleme Güvencesi:** Her işlem öncesinde orijinal oyun dosyasının hash doğrulamalı yedeği alınır. Veri kaybı veya bozulma riski bulunmaz.
- **Anti-Cheat Dostu Güvenlik:** Oyunun çalıştırılabilir dosyalarına (`.exe`), dinamik kütüphanelerine (`.dll`) veya oyun belleğine müdahale edilmez. Üçüncü taraf hook/enjeksiyon içermez.

---

## Güvenlik ve Bütünlük Standartları

- **Salt Veri Katmanı:** Değişiklikler yalnızca metin ve yerelleştirme veri katmanında uygulanır.
- **Format Değişkeni Koruması:** Oyun motoru değişkenleri (`%s`, `{0}`, `<color>` vb.) çeviri aşamasında korunarak metin taşması veya oyun çökmesi engellenmiştir.
- **Tam Geri Alınabilirlik:** Yama kaldırıldığında yedeklenen orijinal paket geri yüklenir ve doğrulama tamamlanır.

---

## Proje Yapısı

```text
Aion2TR/
├── src/
│   ├── Aion2TR.App/            # Modern WPF başlatıcı arayüzü (MVVM)
│   ├── Aion2TR.Core/           # Temel domain modelleri ve arayüzler
│   └── Aion2TR.Infrastructure/ # PAK arşivi, Steam API ve dosya servisleri
├── v2/
│   └── src/
│       └── CleanRuntimeInstaller/ # Atomik yükleme, geri alma ve işlem denetleyicisi
├── tests/
│   └── Aion2TR.Tests/          # Doğrulama testleri
├── Kurulum Paketi/             # Dağıtım paketi şablonu ve patch verileri
├── screenshots/                # Uygulama ekran görüntüleri
├── docs/                       # Teknik mimari ve güvenlik dokümanları
├── LICENSE                     # MIT Açık Kaynak Lisansı
└── Aion2TR.sln                 # .NET Çözüm dosyası
```

---

## Kaynak Koddan Derleme

Geliştiriciler için kaynak koddan derleme adımları:

1. **Gereksinimler:** Windows 10/11, .NET 8 veya .NET 10 SDK
2. **Çözümü Derleme:**
   ```powershell
   dotnet build Aion2TR.sln -c Release
   ```
3. **Testleri Çalıştırma:**
   ```powershell
   dotnet test v2/tests/CleanRuntimeInstaller.Tests/CleanRuntimeInstaller.Tests.csproj
   ```
4. **Tek Dosya (Single-File) Olarak Paketleme:**
   ```powershell
   dotnet publish src/Aion2TR.App/Aion2TR.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```

---

## Lisans ve İletişim

Bu proje [MIT Lisansı](LICENSE) altında sunulmaktadır.

- **Geliştirici:** Furkan Tecir (FuTe)
- **GitHub:** [https://github.com/furkantecir](https://github.com/furkantecir)
- **Proje Deposu:** [https://github.com/furkantecir/AION2_TURKCE_YAMA](https://github.com/furkantecir/AION2_TURKCE_YAMA)
