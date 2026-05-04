# CAVE AI PRO — PC companion

Companion για το **CaveAI Pro (Android)**: φορτώνει τα **ίδια** σπηλαιολογικά δεδομένα που εξάγει / αποθηκεύει το κινητό, και τα **επεξεργάζεται** στο PC (περίληψη έρευνας, εξαγωγή CSV σκοπών).

### Διανομή (Play Store vs PC)

- **CaveAI Pro (Android):** διανέμεται μέσω **Google Play** (όπως ορίζει ο εκδότης).  
- **CAVE AI PRO (έκδοση PC, αυτό το repo):** **δεν** είναι στο Google Play· τρέχει σε **PC με Windows** (x64, Win 10/11). Λήψη/εγκατάσταση μέσω **MSI** ή άλλου καναλιού που ορίζει ο εκδότης. Αν έχεις μόνο το κινητό από το Play, για PC χρειάζεσαι **ξεχωριστό** installer.  
- Νομική/καταστηματική διατύπωση (αγγλικά): `legal/00-DISTRIBUTION-PLATFORMS.md`.

## Συνεργασία με το Android app

| Πηγή στο Android | Τι κάνει η έκδοση PC |
|------------------|-------------------------|
| **Αντίγραφο ασφαλείας ZIP** (`Downloads/CaveAI/CaveAI_Backup_*.zip`) | Διαβάζει το `data.json` μέσα στο ZIP (όπως στο `ProjectBackupZip` του repo `CaveAIPro`). |
| Αρχείο **`caveai_database_v1.json`** (αν το αντιγράψεις από τα internal files του app) | Ίδια δομή: λίστα `CaveProject` σε JSON. |

Μετά: άνοιγμα αρχείου → αυτόματη επιλογή πρώτου project (αν υπάρχει) → περίληψη → καρτέλα **Plan** (centerline + `vectorLines` όπως στο Android) → **Print plan…** (εκτύπωση σχεδίου, προεπισκόπηση εκτυπωτή, landscape) → **Export shots → CSV** (με στήλες LRUD), **Export traverse → Survex (.svx)**, ή (μόνο για ZIP) **Extract photos from ZIP…**.

### ZIP: ακεραιότητα αρχείων

Μετά το άνοιγμα ενός CaveAI backup `.zip`, εμφανίζεται γραμμή κατάστασης που συγκρίνει τα SHA-256 του `integrity_manifest.json` με τα περιεχόμενα του ZIP (όπως στο Android export).

### Τι «κατεβαίνει» με το ZIP στο CAVE AI PRO (Windows)

Όταν ανοίγεις ένα **CaveAI backup `.zip`**, η εφαρμογή Windows διαβάζει πρώτα το **`data.json`** (λίστα `CaveProject` όπως στο Android). Για **Save ZIP / export μόνο του τρέχοντος σπηλαίου** στο Android, το συμβόλαιο είναι **ένα project ανά ZIP** (όχι όλη η βάση)· δες `android-reference/MapsWindowsSync.md` §0. Τα υπόλοιπα αρχεία μέσα στο ZIP **μένουν μέσα στο αρχείο**· το Windows τα ανοίγει **κατά περίπτωση** (π.χ. χάρτης ως underlay, εξαγωγή φωτογραφιών, «Explorer» από την καρτέλα Maps). Η λίστα παρακάτω είναι **ανά project / σπήλαιο** όπως στο JSON (αν το ZIP περιέχει πολλά projects, επαναλαμβάνεται για καθένα).

#### Αρχεία μέσα στο `.zip` (δομή αρχείου)

| Αρχείο / φάκελος | Ρόλος |
|------------------|--------|
| **`data.json`** | Όλα τα δομικά δεδομένα της έρευνας (Gson όπως στο κινητό). |
| **`photos/`** | Αρχεία εικόνας που στο JSON εμφανίζονται ως σχετικά paths `photos/…` (κυρίως φωτογραφίες σκοπιών). |
| **`export_assets/`** | Άλλα τοπικά αρχεία που το Android ενσωματώνει στο ZIP (ήχος σκοπιών, εικόνες Geo/Bio, LIDAR/mesh, κ.λπ.) — paths `export_assets/…` στο `data.json` *(από ενημερωμένο export του `CaveAIPro`)*. **Χάρτες:** ιδανικά κάθε χάρτης σε **δικό του υποφάκελο** μέσα στο ZIP (π.χ. `export_assets/maps/MyCave__a1b2c3d4e5/000_plan_raster_89abcdef/basemap.tif`) ώστε να μην μπερδεύονται μεταξύ τους· δες `android-reference/CaveAiProBackupZipMapExport.kt`. **Συγχρονισμός Android ↔ Windows για χάρτες (backup `.zip` μόνο):** `android-reference/MapsWindowsSync.md`. **Για διαχείριση στο PC / γραφείο:** προτίμησε **raster** (`.png`, `.tif`, `.jpg`, `.webp`, `.bmp`)· το **PDF** (πρώτη σελίδα) υποστηρίζεται και ως underlay στο Windows companion. |
| **`backup_manifest.json`** | Μεταδεδομένα export (έκδοση, ώρα, λειτουργία «τρέχον project / όλα»). |
| **`integrity_manifest.json`** | SHA-256 ανά αρχείο στο ZIP· το Windows το ελέγχει για ακεραιότητα. |
| **`map_inventory.json`** | *(Νεότερο export Android)* Αναλυτική λίστα χαρτογραφικών διαδρομών ανά σπήλαιο — διαβάζεται στην καρτέλα **Backup detail**. |
| **`README.txt`** | Σύντομη επεξήγηση της δομής του backup. |

#### Τι περιέχει το `data.json` ανά σπήλαιο (λογικές ενότητες)

Όσα **ονομάζονται ρητά** στο μοντέλο Windows (`CaveProjectDocument` / `ShotRecord`) και όσα έρχονται ως **υπόλοιπο JSON** (`ExtensionData`) — το Android μπορεί να γράφει περισσότερα κλειδιά· το PC τα κρατά και τα σκανάρει όπου χρειάζεται (χάρτες, κατάλογοι).

1. **Ταυτότητα & μεταδεδομένα project**  
   `name`, `date`, `startTime`, `endTime`, συντεταγμένες εισόδου (`lat`, `lon`, `alt`), `shots` (λίστα), `vectorLines`, `rocks`, `fieldCatalogEntries`, και μέσω **ExtensionData** π.χ. `exitLat`/`exitLon`/`exitAlt`, `trackPoints`, `linkedLibraryCaveId`, `surveyEventLog`, `vehiclePark*`, `visitBaselineFingerprintJson`, `excludeEntranceCoordsFromPublicPublish`, `requireVehicleParkStep`, σκίτσα (`sketches`, `sectionSketches`), `mapSymbols`, `brackets`, `depthSpanAnnotations`, `surfaceLidarRaster`, `publicLibraryCartographyUris`, `cartographyTlsMeshObjUri`, `surveyArchiveSchemaVersion`, `surveyArchivedAtMs`, κ.ά. (όπως στο `CaveSurveyModels.kt` του Android).

2. **Σκοπιές (`shots[]`)**  
   Γεωμετρία traverse (`fromStation`, `toStation`, `distance`, `azimuth`, `clino`), LRUD (`l`, `r`, `u`, `d`), `notes`, `depth`, `symbol`, splays/radials όπου υπάρχουν στο JSON.  
   Επιπλέον στο **ίδιο αντικείμενο JSON** (ακόμη κι αν δεν είναι ξεχωριστά πεδία στο C# `ShotRecord`): **`photos[]`** (paths ή URIs), **`audioMemoUri`**, τιμές BLE / χειροκίνητες (`atmosphericO2VolPct`, `ambientBleTempCelsius`, `manualAmbientTempCelsius`, `ambientBleRelativeHumidityPct`, `manualRelativeHumidityPct`, κ.λπ.).

3. **Geo/Bio — δείγματα βράχου (`rocks[]`)**  
   Π.χ. `imageUri`, `station`, τίτλος/περιγραφή ανάλυσης, `planMapX` / `planMapY`, κ.λπ.

4. **Κατάλογος πεδίου / οργανισμοί (`fieldCatalogEntries[]`)**  
   Κατηγορία, όνομα, σημειώσεις, επιστημονικό όνομα, αφθονία, `photoReference`, θέση σε χάρτη (`planMapX`, `planMapY`), κ.λπ.

5. **Χαρτογραφία & σχέδιο**  
   `vectorLines` (γραμμές plan/section/profile), URIs χαρτών/επικαλύψεων μέσα στο JSON (συλλέγονται δυναμικά για την καρτέλα **Maps**).

#### Τι κάνει η έκδοση Windows με όλα αυτά

| Δεδομένο / αρχείο | Στο Windows (ενδεικτικά) |
|-------------------|---------------------------|
| `data.json` (όλο το project) | Φόρτωση projects, περίληψη έρευνας, λίστα σκοπιών, **Export shots → CSV**, **Export traverse → Survex (.svx)**. |
| `shots` + `vectorLines` | Καρτέλες **Plan** / **Section** (centerline + διανύσματα), εκτύπωση σχεδίου όπου υπάρχει. |
| Paths `photos/…`, `export_assets/…`, `https://…` κ.λπ. στο JSON | Καρτέλα **Maps** (λίστα assets, άνοιγμα στο Explorer, εξαγωγή αρχείου, **raster underlay** όταν το αρχείο αποκωδικοποιείται — συμπεριλαμβανομένου cache για https εικόνες όπου υποστηρίζεται). |
| `rocks` + `fieldCatalogEntries` | **Cave registry** (σύνοψη ανά σπήλαιο) και **Bio/Mineral catalog** (γραμμές από βράχους + καταχωρήσεις πεδίου). |
| `integrity_manifest.json` + περιεχόμενα ZIP | **Έλεγχος ακεραιότητας** (banner κατάστασης). |
| Μόνο φάκελος **`photos/`** | Εντολή **Extract photos from ZIP…** (αντιγραφή `photos/**` σε φάκελο που επιλέγεις). Τα **`export_assets/**`** δεν αντιγράφονται από αυτή την εντολή· χρησιμοποίησε την καρτέλα Maps / «Extract» ανά asset. |

**Σημείωση:** Αν ανοίξεις **μόνο** `caveai_database_v1.json` χωρίς ZIP, τα τοπικά αρχεία που στο κινητό ήταν `content://` δεν υπάρχουν στο PC — χρειάζεται **πλήρες ZIP** με `photos/` και `export_assets/` (όπως το νέο export του Android) για να δουλεύουν οι ίδιες διαδρομές αρχείων.

### Survex

Το `.svx` περιέχει μόνο **traverse** σκοπιές (`*data normal from to tape compass clino`) και ένα προσωρινό `*fix` στον πρώτο σταθμό `0 0 0` — **έλεγξε** συμβάσεις/μονάδες πριν από παραγωγική χρήση (Survex/Cavern).


## Build / Run

### Κανονική εγκατάσταση Windows (MSI)

Για **κανονική εφαρμογή** (χωρίς παράθυρο CMD), χτίσε το installer και τρέξε το MSI:

1. Εγκατάσταση [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) στο PC που **χτίζεις** το πακέτο.
2. Διπλό κλικ **`BuildInstaller.bat`** (ή `dotnet build installer\CaveAiProForWindows.Installer.wixproj -c Release`).
3. Ανοίγεις **`installer\bin\Release\CaveAiProForWindows-Setup.msi`** σε **Windows PC** (όχι από Google Play): εγκατάσταση σε `Program Files`, συντόμευση στο μενού Έναρξη, self-contained (δεν χρειάζεται ξεχωριστό .NET στον υπολογιστή-στόχο). Η απεγκατάσταση γίνεται από Ρυθμίσεις → Εφαρμογές.

Το **`run.bat`** ανοίγει την εγκατεστημένη εφαρμογή (MSI ή προηγούμενη per-user εγκατάσταση) χωρίς να κρατάει κονσόλα.

Αν «δεν ανοίγει τίποτα» ή εμφανίζεται σφάλμα: δοκίμασε **`TryApp.bat`** (self-contained **φάκελος**, όχι single-file). Αν εμφανιστεί μήνυμα σφάλματος, άνοιξε και το **`%LOCALAPPDATA%\CaveAiProForWindows\last-error.txt`** (πλήρες stack trace). Υπάρχει και **`startup.log`** στον ίδιο φάκελο. Μετά **`BuildInstaller.bat`** και ξανά εγκατάσταση MSI. Αν είχες παλιό `dist\TryRun`, το `TryApp.bat` ξαναχτίζει αν λείπει το `.dll` εκεί.

### Ανάπτυξη από πηγή

```bat
cd /d d:\caveaiproforwindows
dotnet build
dotnet run --project src\CaveAiProForWindows\CaveAiProForWindows.csproj
```

Εναλλακτικά: **`Install.bat`** / **`Install-SelfContained.bat`** (PowerShell) για εγκατάσταση μόνο στο προφίλ σου, χωρίς MSI.

## Δομή κώδικα

- `Models/` — υποσύνολο πεδίων συμβατό με Gson του Android (`Shot`, `CaveProject`).
- `Services/ExplorationDataLoader.cs` — `*.json` ή `*.zip`.
- `Services/ExplorationAnalytics.cs` — περίληψη + CSV UTF-8 BOM.
- `Services/IntegrityVerifier.cs` — επαλήθευση `integrity_manifest.json`.
- `Services/ZipPhotoExtractor.cs` — εξαγωγή `photos/**` από ZIP.
- `Services/SurvexExporter.cs` — ελάχιστο `.svx` traverse centerline.
- `Services/SurveyStationGeometry.cs` — ίδια μείωση σταθμών με `calculateCaveCoordinates` (Android) + ανάγνωση `vectorLines` για plan.
- `Views/PlanView.*` — 2D προβολή σχεδίου.

## Πορεία: τοπογραφικό εργαλείο σπηλαίων (μαζί με CaveAI Pro — Android)

**Ρόλοι:** το **κινητό (`d:\caveaipro`)** μένει κέντρο καταγραφής, χάρτη, σχεδίαση (`vectorLines`), DXF από το app, backup ZIP. Το **Windows** επεκτείνεται σε σταθμό εργασίας: ίδιο `data.json` / ZIP, περίληψη, Survex/CSV, ακεραιότητα, και σταδιακά **σχέδιο + QC** σε μεγάλη οθόνη.

| Φάση | Στόχος |
|------|--------|
| **1 — Δεδομένα** | Πλήρης ανάγνωση πεδίων χάρτη από JSON (`vectorLines` ήδη αναγνωρίζεται· περίληψη με πλήθος γραμμών). Στο Android: σταθερά exports / έκδοση schema όπου χρειάζεται. |
| **2 — Σχέδιο PC** | Canvas 2D plan (ίδιο survey frame με το app): centerline από σκοπιές + εμφάνιση `vectorLines` ανά `viewMode`. |
| **3 — Εξαγωγές** | DXF/SVG συγχρονισμένα με `DxfExport.kt`· βελτίωση Survex (μονάδες, σταθμοί)· προαιρετικά Therion `.th` αργότερα. |
| **4 — QC** | Loop closure / μήκος / υποδείξεις αντιστοίχισης με λογική Android (όπου υπάρχει στο repo). |

Άμεσες ιδέες κώδικα: Therion `.th`, πλούσιο import `vectorLines`/`mapSymbols`, ευθυγράμμιση DXF Windows↔Android.

### CaveAI Pro (Android)

Κύριο manual: στο repo `caveaipro` → `docs/CARTOGRAPHY_AND_USAGE_GUIDE.md` (survey frame, Map tab, exports). Το μοντέλο `VectorLine` / `CaveProject` ορίζεται στο `app/.../CaveSurveyModels.kt`.
