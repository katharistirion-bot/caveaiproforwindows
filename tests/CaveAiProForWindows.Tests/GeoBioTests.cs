using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class GeoBioTests
{
    [TestMethod]
    public void Service_treats_rocks_array_entries_as_geology_records()
    {
        var p = new CaveProjectDocument
        {
            Rocks = JsonDocument.Parse("""
                                       [
                                           {"name":"Calcite stalactite","caveAiAnalysisText":"Pure calcite, c. 200ka.","photoUri":"photos/rocks/r1.jpg"},
                                           {"mineralName":"Aragonite needles","analysis":"Acicular aragonite mass.","images":["photos/rocks/r2.jpg","photos/rocks/r3.jpg"]}
                                       ]
                                       """).RootElement,
        };

        var records = GeoBioRecordsService.Build(p);

        Assert.AreEqual(2, records.Count);
        Assert.IsTrue(records.All(r => r.Category == GeoBioCategory.Rock),
            "rocks[] entries should default to Rock category.");
        Assert.AreEqual("Calcite stalactite", records[0].Title);
        Assert.AreEqual("Pure calcite, c. 200ka.", records[0].CaveAiAnalysisText);
        StringAssert.Contains(records[0].SourceLabel, "rocks[0]");
        Assert.AreEqual(2, records[1].ImageReferences.Count);
        Assert.AreEqual("photos/rocks/r2.jpg", records[1].ImageReferences[0]);
    }

    [TestMethod]
    public void Service_classifies_organism_records_from_field_catalog_using_keywords()
    {
        var p = new CaveProjectDocument
        {
            FieldCatalogEntries = JsonDocument.Parse("""
                                                     [
                                                         {"species":"Niphargus sp.","commonName":"cave amphipod","analysisText":"Troglobitic crustacean."},
                                                         {"name":"Calcite vein","mineralName":"Calcite","summary":"Vein in limestone."}
                                                     ]
                                                     """).RootElement,
        };

        var records = GeoBioRecordsService.Build(p);

        Assert.AreEqual(2, records.Count);
        Assert.AreEqual(GeoBioCategory.Organism, records[0].Category);
        Assert.AreEqual(GeoBioCategory.Rock, records[1].Category);
    }

    [TestMethod]
    public void Service_reads_explicit_geoBioRecords_extension_array()
    {
        var p = new CaveProjectDocument
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["geoBioRecords"] = JsonDocument.Parse("""
                                                      [
                                                        {"category":"Rock","name":"Speleothem-A","caveAiAnalysisText":"Stalactite tip.","imageReferences":["photos/geo/a.jpg"],"station":"S12"},
                                                        {"category":"Organism","name":"Bat colony","caveAiAnalysisText":"Rhinolophus sp.","imageReferences":["photos/bio/bat1.jpg"],"station":"S03"}
                                                      ]
                                                      """).RootElement,
            },
        };

        var records = GeoBioRecordsService.Build(p);

        Assert.AreEqual(2, records.Count);
        Assert.AreEqual(GeoBioCategory.Rock, records[0].Category);
        Assert.AreEqual("S12", records[0].Station);
        Assert.AreEqual(GeoBioCategory.Organism, records[1].Category);
        Assert.AreEqual("S03", records[1].Station);
        Assert.AreEqual(1, records[1].ImageReferences.Count);
    }

    [TestMethod]
    public void Service_reads_singular_imageUri_when_no_array_present()
    {
        var p = new CaveProjectDocument
        {
            Rocks = JsonDocument.Parse("""
                                       [{"name":"Sample","imageUri":"photos/rocks/single.jpg"}]
                                       """).RootElement,
        };

        var records = GeoBioRecordsService.Build(p);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual(1, records[0].ImageReferences.Count);
        Assert.AreEqual("photos/rocks/single.jpg", records[0].ImageReferences[0]);
    }

    [TestMethod]
    public void Service_deduplicates_image_references()
    {
        var p = new CaveProjectDocument
        {
            Rocks = JsonDocument.Parse("""
                                       [{"name":"Sample","imageUri":"photos/rocks/x.jpg","images":["photos/rocks/x.jpg","photos/rocks/y.jpg"]}]
                                       """).RootElement,
        };

        var records = GeoBioRecordsService.Build(p);

        Assert.AreEqual(2, records[0].ImageReferences.Count, "Duplicate references should collapse.");
        CollectionAssert.AreEqual(new[] { "photos/rocks/x.jpg", "photos/rocks/y.jpg" },
            records[0].ImageReferences.ToArray());
    }

    [TestMethod]
    public void Service_uses_explicit_category_field_for_mixed_buckets()
    {
        var p = new CaveProjectDocument
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["geoBioRecords"] = JsonDocument.Parse("""
                                                      [{"category":"organism","name":"Cricket","analysis":"Troglophile."}]
                                                      """).RootElement,
            },
        };
        var records = GeoBioRecordsService.Build(p);
        Assert.AreEqual(GeoBioCategory.Organism, records[0].Category);
    }

    [TestMethod]
    public void FieldCatalogEntryParser_reads_android_kind_and_taxonomy_fields()
    {
        var el = JsonDocument.Parse("""
            {
              "kind":"BACTERIA",
              "name":"Sulfur-oxidizing biofilm",
              "scientificName":"Thiobacillus sp.",
              "category":"Chemolithotroph",
              "stationTag":"S12",
              "abundance":"common",
              "microhabitat":"pool margin",
              "extendedAnalysis":"Thin bacterial mat on flowstone.",
              "photoReference":"photos/catalog/b1.jpg",
              "recordedAt":"06 Jun 2026, 14:30"
            }
            """).RootElement;

        var rec = FieldCatalogEntryParser.TryParse(el, "fieldCatalogEntries[0]");
        Assert.IsNotNull(rec);
        Assert.AreEqual(FieldCatalogEntryKind.Bacteria, rec!.FieldKind);
        Assert.AreEqual(GeoBioCategory.Organism, rec.Category);
        Assert.AreEqual("Thiobacillus sp.", rec.ScientificName);
        Assert.AreEqual("Chemolithotroph", rec.TaxonomicGroup);
        Assert.AreEqual("S12", rec.Station);
        StringAssert.Contains(rec.CaveAiAnalysisText!, "bacterial mat");
        Assert.AreEqual(1, rec.ImageReferences.Count);
    }

    [TestMethod]
    public void Service_classifies_BIOTA_kind_as_organism()
    {
        var p = new CaveProjectDocument
        {
            FieldCatalogEntries = JsonDocument.Parse("""
                [{"kind":"BIOTA","name":"Greater horseshoe bat","scientificName":"Rhinolophus ferrumequinum","category":"Chiroptera"}]
                """).RootElement,
        };
        var records = GeoBioRecordsService.Build(p);
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual(GeoBioCategory.Organism, records[0].Category);
        Assert.AreEqual(FieldCatalogEntryKind.Biota, records[0].FieldKind);
    }

    [TestMethod]
    public void BackupPhotoIndexer_returns_empty_when_project_and_zip_are_missing()
    {
        var entries = BackupPhotoIndexer.Build(project: null, zipPath: null);
        Assert.AreEqual(0, entries.Count);
    }
}
