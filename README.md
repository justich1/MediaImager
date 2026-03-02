# Media Imager

**Media Imager** je jednoduchý Windows nástroj pro **zálohu a obnovu (image) fyzických disků a médií** (SD karta, USB disk, HDD/SSD). Umí vytvořit **komprimovaný obraz disku** a stejný obraz zpět **obnovit na vybraný disk**.

> ⚠️ Obnova je destruktivní operace: přepíše celý cílový disk (včetně oddílů). Vybírej disk velmi opatrně.

---

## Co to umí

- Automaticky načte seznam fyzických disků přes WMI (`Win32_DiskDrive`)
- **Záloha (Backup)** vybraného disku do souboru:
  - čte přímo z `\\.\PHYSICALDRIVE{N}`
  - ukládá jako **gzip** (komprimovaný raw image)
- **Obnova (Restore)** ze souboru zpět na disk:
  - otevře cílový disk pro zápis
  - pokusí se disk nastavit jako **offline** během obnovy (kvůli bezpečnosti)
  - zapisuje data přímo na zařízení (WinAPI `WriteFile`)
  - na konci nastaví disk zpět **online**
- Průběh v UI (progress + status)

---

## Formát zálohy

Výsledný soubor je **gzip stream** obsahující **raw image** disku.

- Výhody: komprese, jednoduchost
- Nevýhody: záloha obsahuje celý disk včetně prázdných míst (komprese s nulami ale obvykle hodně pomůže)

---

## Požadavky

### Windows
- Windows 10/11
- **Administrátorská práva** (přístup k `\\.\PHYSICALDRIVE*`)
- .NET 8 (pokud spouštíš build vyžadující runtime)

### Pro vývoj
- Visual Studio 2022 / .NET SDK 8
- Projekt: `net8.0-windows`, WinForms

---

## Instalace / spuštění

### Build ve Visual Studiu
1. Otevři `SDCardBackupRestore.sln`
2. Build / Run (ideálně „Spustit jako administrátor“)

### Build přes CLI
```bash
dotnet restore
dotnet build -c Release
```

---

## Použití

### Záloha (Backup)
1. Spusť aplikaci jako **Administrátor**
2. V seznamu vyber disk (např. `\\.\PHYSICALDRIVE0`)
3. Klikni **Backup**
4. Vyber cílový soubor (kam se uloží gzip image)
5. Počkej na dokončení

### Obnova (Restore)
1. Spusť aplikaci jako **Administrátor**
2. Vyber cílový disk (na který bude image obnovena)
3. Klikni **Restore**
4. Vyber záložní soubor (gzip image)
5. Potvrď – data na cílovém disku budou přepsána

> Doporučení: před obnovou odpoj ostatní externí disky, aby se minimalizovalo riziko záměny.

---

## Jak to funguje technicky (stručně)

- Disky se zjišťují přes WMI:
  - `SELECT ... FROM Win32_DiskDrive`
- Přístup k disku:
  - `CreateFile("\\\\.\\PHYSICALDRIVE{N}", ...)`
- Backup:
  - čte stream z disku a zapisuje do `GZipStream(CompressionMode.Compress)`
- Restore:
  - čte `GZipStream(CompressionMode.Decompress)`
  - zapisuje na zařízení přes `WriteFile` (přímý zápis)
  - během obnovy nastaví disk offline/online (přes IOCTL disk attributes)

Aplikace si při obnově vytváří dočasný soubor:
- `temp_restore_file.bin` (v adresáři aplikace)

---

## Bezpečnost a rizika

- Restore může přepsat špatný disk → vždy kontroluj, co je vybrané.
- Přímý zápis na disk vyžaduje admin práva.
- Obnova může změnit tabulku oddílů → Windows může po dokončení chtít „správu disků“ / restart / znovupřipojení.

---

## Troubleshooting

### „Nepodařilo se otevřít zařízení… Spouštíte program jako administrátor?“
- Spusť aplikaci **jako Administrátor**

### Disk nejde nastavit offline
- Disk může být používán (otevřené soubory, připojený oddíl)
- Zkus:
  - zavřít aplikace používající disk
  - odpojit/připojit médium
  - případně použít Správu disků

### Obnova trvá dlouho / pomalé
- Limit může být:
  - rychlost média (SD/USB)
  - výkon CPU (dekomprese)
  - rychlost zápisu disku

---
