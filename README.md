# HELLDRIVE

Extreme Game Performance Utility.

## Gereksinim

- .NET 8 SDK
- Windows
- DirectX bağımlılığı yoktur.

## Derleme

```powershell
dotnet restore
dotnet build -c Release
```

Tek dosya EXE:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Çıktı:

`bin\Release\net8.0-windows\win-x64\publish\HellDrive.exe`

## Not

Bu prototip profil/arayüz, oyun başlatma, process önceliği ve güvenli yedekleme altyapısını içerir.

Oyunların grafik ayarları birbirinden farklı olduğu için config dosyalarını rastgele değiştirmez. Gerçek 360p/texture/shadow değişiklikleri oyun bazlı adapter sistemiyle eklenmelidir.
