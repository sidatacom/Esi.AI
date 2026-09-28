# C# Dev Kit lokal recherchieren

Die Marketplace-Erweiterung ist ein VSIX-Paket, kein vollständiger Upstream-Quellcode. Für zulässige lokale Recherche kann eine konkrete Version direkt aus dem offiziellen [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) geladen und entpackt werden.

Voraussetzungen: `curl` und `unzip`.

Im Repository-Root ausführen; `VERSION` bei Bedarf auf eine verfügbare Version setzen:

```bash
VERSION=1.15.2
TARGET="origins/ms-dotnettools.csdevkit-${VERSION}"
mkdir -p "$TARGET"
curl --fail --location \
  --user-agent "VSCode" \
  "https://ms-dotnettools.gallery.vsassets.io/_apis/public/gallery/publisher/ms-dotnettools/extension/csdevkit/${VERSION}/assetbyname/Microsoft.VisualStudio.Services.VSIXPackage?targetPlatform=linux-x64" \
  --output "$TARGET/csdevkit.vsix"
unzip -o "$TARGET/csdevkit.vsix" -d "$TARGET"
```

Lies vor der Nutzung die Lizenzdatei `extension/LICENSE.md` im entpackten Paket und beachte zusätzlich die [Marketplace-Nutzungsbedingungen](https://aka.ms/vsmarketplace-ToU). Das VSIX und seine Inhalte dürfen laut den beigefügten Bedingungen nicht weitergegeben oder veröffentlicht werden; Reverse Engineering, Dekompilieren und Disassemblieren sind ebenfalls eingeschränkt. Die versionsbezogenen lokalen Ordner `ms-dotnettools.csdevkit-*` werden deshalb von Git ignoriert. Nicht einchecken oder veröffentlichen.