# Χάρτες: συγχρονισμός Android ↔ Windows — **μόνο ZIP backup**

**Scope:** Αυτό το έγγραφο αφορά **αποκλειστικά** το **CaveAI Pro backup `.zip`** (το ίδιο αρχείο που ανοίγεις στο Windows). Δεν ορίζει ροή «μόνο `data.json` χωρίς ZIP», ούτε αυτόνομα αρχεία χαρτών από File → Open· αυτά είναι ξεχωριστά workflows στο PC.

Στόχος: ό,τι το Android **ενσωματώνει στο ZIP** για χαρτογραφία να **εμφανίζεται** στην καρτέλα **Maps**, να **εξάγεται** από το ZIP (temp / Explorer) και —όπου είναι raster/PDF— να **δουλεύει ως Plan/Section underlay** όταν το **ίδιο** `.zip` είναι ανοιχτό στο companion.

**Πηγή αλήθειας για ZIP maps:** `MapAssetsCollector` (paths μέσα στο `data.json` **του ZIP**), `ZipMapInventoryReader`, `MapAssetOpener`, `PlanMapUnderlayLoader`, `RasterImageDecoder`.

**Πλούσιο survey JSON (AI / QC / strokes):** `DataJsonSurveySchemaV2.md` + `CaveProjectDataJsonEnrichment.kt` — επέκταση `data.json` πέρα από χάρτες· το Windows deserialize-άρει όλα τα νέα πεδία στο `CaveProjectDocument` / `ShotRecord`.

---

## 0. Save ZIP — **μόνο το σπήλαιο που είναι ανοιχτό εκείνη τη στιγμή**

Όταν ο χρήστης κάνει **αποθήκευση / export backup ZIP** για την **τρέχουσα** έρευνα, το αρχείο ZIP πρέπει να αφορά **αποκλειστικά αυτό το ένα σπήλαιο/project** — όχι άλλες έρευνες από τη συσκευή.

| Τι | Απαίτηση |
|-----|----------|
| **`data.json`** | Να περιέχει **μόνο ένα** στοιχείο στο top-level array `CaveProject[]` (ή ισοδύναμο single-project wrapper που ήδη υποστηρίζει το `ExplorationDataLoader`). **Όχι** πλήρης export όλων των σπηλαίων της βάσης μέσα στο ίδιο ZIP αυτής της ροής. |
| **`map_inventory.json`** | Αν υπάρχει: το `projects[]` να έχει **μία** εγγραφή — αυτή του τρέχοντος `projectName` / `projectDate`. |
| **`photos/`**, **`export_assets/`** | Μόνο αρχεία και paths που **ανήκουν** σε αυτό το project (όχι φωτογραφίες/χάρτες άλλων σπηλαίων). |
| **`integrity_manifest.json`** | Να καλύπτει **ακριβώς** τα entries που μπήκαν σε αυτό το ZIP (όχι orphan hashes από άλλα projects). |

Αν το προϊόν Android υποστηρίζει **και** «export όλων» σε ένα ZIP, αυτό είναι **ξεχωριστή** εντολή/ρόλος· η ροή **Save ZIP / backup του τρέχοντος σπηλαίου** ορίζεται εδώ ως **single-project archive** για σαφήνεια στο Windows companion.

---

## 1. ZIP και διαδρομές

- Το Windows φορτώνει projects από **`data.json` ως ZIP entry** (όχι από εξωτερικό αρχείο όταν μιλάμε για αυτό το συμβόλαιο).
- Στο `data.json` **μέσα στο ZIP**, **όλες** οι διαδρομές χαρτών που δείχνουν σε αρχείο **στο ίδιο ZIP** πρέπει να είναι **σχετικές** με forward slashes, π.χ.  
  `export_assets/maps/MyCave__abc1234567/000_plan_raster_deadbeef/basemap.png`  
  **Όχι** `content://…` — το Windows **δεν** τα επιλύει **από ZIP**.
- Τα ονόματα entries στο ZIP πρέπει να **ταιριάζουν** ακριβώς (με `/`) με αυτά που γράφει το JSON· προτίμησε **case-sensitive** writers (το Windows κάνει case-insensitive fallback όπου μπορεί).
- Υλοποίηση αντιγραφής + rewrite paths: `CaveAiProBackupZipMapExport.kt` (`bundleMapsForZipProject`).
- **PC companion (Windows):** Αν το `data.json` έχει **μόνο leaf** TIFF (π.χ. `0001_site.tif`) χωρίς πλήρες path, το `PlanMapUnderlayLoader` ψάχνει δίπλα στο ανοιγμένο `.json` ή `.zip` σε φακέλους `cartography`, … (βλ. κώδικα). Για δέντρα `maps/plan/`, `maps/section/` με αρχείο σήμανσης **`_CaveAI_folder_marker.txt`** στον ίδιο φάκελο με τα rasters, χρησιμοποιείται `CaveMapsMarkerPathResolver` (αναδρομική εύρεση marker + συνδυασμός σχετικών paths). Debug: `[MapsMarker]`, `[PlanMapUnderlay]`.

---

## 2. `map_inventory.json` (προαιρετικό αλλά συνιστάται)

Ρίζα ZIP, αρχείο: **`map_inventory.json`** (ή nested με leaf name `map_inventory.json` — το Windows ψάχνει με `NormalizeZipEntryPath`).

### Σχήμα JSON (αυστηρά — ταιριάζει με `ZipMapInventoryReader`)

```json
{
  "projects": [
    {
      "projectName": "Ίδιο string με CaveProject.name στο data.json",
      "projectDate": "Ίδιο string με CaveProject.date στο data.json",
      "maps": [
        {
          "slot": "ανθρώπινη ετικέτα, π.χ. plan_raster",
          "pathInBackupOrUrl": "export_assets/maps/.../file.png ή https://...",
          "storageKind": "zip | https | file"
        }
      ]
    }
  ]
}
```

- **`projectName` / `projectDate`**: πρέπει να **ταιριάζουν** με το project στο `data.json` (το Windows κάνει case-insensitive compare στο `projectName` με `CaveProjectDocument.Name`). Απόκλιση = η γραμμή inventory **δεν** δένει με το σπήλαιο στο Plan.
- **`pathInBackupOrUrl`**: για τοπικά αρχεία μέσα στο ZIP, ίδιο path όπως στο ZIP entry.
- **`storageKind`**: ενημερωτικό για UI· για αρχεία μέσα στο ZIP χρησιμοποίησε `"zip"`.

Στο **`backup_manifest.json`** βάλε `"includes_map_inventory": true` όταν υπάρχει inventory (`BackupManifestReader` στο Windows το εμφανίζει).

---

## 3. `data.json` (entry μέσα στο ZIP) — κλειδιά χαρτογραφίας

Αυτά είναι **explicit** στο `MapAssetsCollector` (ExtensionData / δομές):

| Πηγή | Περιγραφή |
|------|------------|
| `publicLibraryCartographyUris` | Array από strings (URI/path ανά δείκτη). |
| `cartographyTlsMeshObjUri` | String (συχνά `.obj`). |
| `surfaceLidarRaster` | Αντικείμενο/πίνακας — αναδρομική συλλογή strings. |
| `mapSymbols`, `sketches`, `sectionSketches`, `trackPoints` | Deep harvest paths (nested `uri`/`path`/εικόνα κ.λπ. — βλ. `HarvestAssetStrings`). |
| `vectorLines` | Deep harvest (embedded refs αν υπάρχουν). |
| `rocks[].imageUri` | Για Maps όταν χρειάζεται πλήρης λίστα (και CSV report). |
| `fieldCatalogEntries[].photoReference` | Αν μοιάζει με asset path/URI. |

Επιπλέον, **οποιοδήποτε** κλειδί ExtensionData που:

- περιέχει `cartograph`, `raster`, `lidar`, `basemap`, `ortho`, `geojson`, `kml`, ή `layer`+`map`, ή
- ταιριάζει σε `LooksLikeMapRelatedKey` (π.χ. `…MapUri`, `plan…image…`),

σαρώνεται με `CollectFromJsonValue` (strings, arrays, objects).

Τέλος, string τιμές σε κλειδιά που τελειώνουν σε **`Uri` / `Url`** ή περιέχουν path με `/`, `http`, `file:`, κ.λπ. προστίθενται αν περνούν `IsProbableAssetPathOrUri`.

---

## 4. Μορφές αρχείων μέσα στο ZIP (underlay)

Όταν το path δείχνει σε αρχείο **μέσα στο ανοιχτό backup ZIP**, το **Plan/Section underlay** χρησιμοποιεί `RasterImageDecoder`: PNG, JPEG, WebP, GIF, BMP, TIFF/GeoTIFF (πολλές παραλλαγές), **PDF (πρώτη σελίδα)**.

Για **μικρότερο και πιο προβλέψιμο** ZIP, προτίμησε **PNG ή TIFF** για basemap· PDF επιτρέπεται.

---

## 5. Checklist Android (χάρτες **μέσα στο ZIP**)

1. [ ] Κατά το ZIP export: για κάθε τοπικό χάρτη, **αντιγραφή bytes** κάτω από `export_assets/maps/...`.
2. [ ] **Αντικατάσταση** στο Gson JSON κάθε `content://` / εσωτερικής διαδρομής με το νέο `export_assets/maps/...` path.
3. [ ] (Συνιστάται) **`map_inventory.json`** με `projects[]` όπως παραπάνω, `includes_map_inventory` στο manifest.
4. [ ] **`integrity_manifest.json`** ενημερωμένο για όλα τα νέα entries.
5. [ ] **`projectName`/`projectDate`** στο inventory = ίδια με το `data.json` για το ίδιο cave.

Αν ακολουθείς τα παραπάνω, τα χαρτογραφικά **μέσα στο ZIP** από το Android **μένουν συγχρονισμένα** με αυτό που το Windows companion διαβάζει **όταν ανοίγεις το `.zip`** — χωρίς αλλαγή στο Gson μοντέλο πέρα από paths + optional inventory.
