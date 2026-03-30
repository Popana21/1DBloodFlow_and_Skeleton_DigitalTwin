using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds anatomical placement and orientation info for a vessel.
/// region: head, neck, upper_chest, chest, spine, hips, upper_leg, lower_leg, shoulder, upper_arm, lower_arm
/// side: left, right, midline
/// direction: suggested base direction within that region (used by mappers/ontology)
/// </summary>
public class VesselOntology
{
    public string region;       // e.g. "upper_arm"
    public string side;         // "left", "right", "midline"
    public Vector3 direction;   // preferred direction (rough anatomical axis)
}

/// <summary>
/// Map vessel name keywords to approximate anatomical region + preferred direction and side.
/// Robust to duplicates and normalizes input to lower-case.
/// </summary>
public static class VesselOntologyParser
{
    // keyword -> ontology
    private static readonly Dictionary<string, VesselOntology> keywordOntology;

    static VesselOntologyParser()
    {
        keywordOntology = new Dictionary<string, VesselOntology>(StringComparer.OrdinalIgnoreCase);

        void AddIfMissing(string key, VesselOntology ont)
        {
            if (string.IsNullOrEmpty(key) || ont == null) return;
            if (!keywordOntology.ContainsKey(key))
                keywordOntology.Add(key, ont);
        }

        // HEAD
        AddIfMissing("external_carotid", new VesselOntology { region = "head", direction = Vector3.up });
        AddIfMissing("internal_carotid", new VesselOntology { region = "head", direction = Vector3.up });

        // NECK
        AddIfMissing("vertebral",      new VesselOntology { region = "neck", direction = Vector3.up });
        AddIfMissing("common_carotid", new VesselOntology { region = "neck", direction = Vector3.up });

        // UPPER CHEST / CHEST / SPINE / HIPS (central trunk)
        AddIfMissing("aortic_arch",         new VesselOntology { region = "upper_chest", direction = Vector3.up });
        AddIfMissing("thoracic_aorta",      new VesselOntology { region = "chest",       direction = Vector3.down });
        AddIfMissing("abdominal_aorta",     new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("celiac",              new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("hepatic",             new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("splenic",             new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("gastric",             new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("superior_mesenteric", new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("inferior_mesenteric", new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("renal",               new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("iliac",               new VesselOntology { region = "hips",        direction = Vector3.down });

        // SHOULDERS / ARMS
        AddIfMissing("subclavian",            new VesselOntology { region = "shoulder",   direction = Vector3.down });
        AddIfMissing("axillary",              new VesselOntology { region = "upper_arm",  direction = Vector3.down });
        AddIfMissing("brachial",              new VesselOntology { region = "upper_arm",  direction = Vector3.down });
        AddIfMissing("radial",                new VesselOntology { region = "lower_arm",  direction = Vector3.down });
        AddIfMissing("ulnar",                 new VesselOntology { region = "lower_arm",  direction = Vector3.down });
        AddIfMissing("interosseous",          new VesselOntology { region = "lower_arm",  direction = Vector3.down });
        AddIfMissing("posterior_interosseous",new VesselOntology { region = "lower_arm",  direction = Vector3.down });

        // LEGS
        AddIfMissing("femoral",            new VesselOntology { region = "upper_leg", direction = Vector3.down });
        AddIfMissing("profunda_femoris",   new VesselOntology { region = "upper_leg", direction = Vector3.down });
        AddIfMissing("popliteal",          new VesselOntology { region = "upper_leg", direction = Vector3.down });
        AddIfMissing("tibial",             new VesselOntology { region = "lower_leg", direction = Vector3.down });
        AddIfMissing("tibiofibular",       new VesselOntology { region = "lower_leg", direction = Vector3.down });
        AddIfMissing("anterior_tibial",    new VesselOntology { region = "lower_leg", direction = Vector3.down });
        AddIfMissing("posterior_tibial",   new VesselOntology { region = "lower_leg", direction = Vector3.down });

        // ILIAC
        AddIfMissing("common_iliac",   new VesselOntology { region = "hips", direction = Vector3.down });
        AddIfMissing("external_iliac", new VesselOntology { region = "hips", direction = Vector3.down });
        AddIfMissing("internal_iliac", new VesselOntology { region = "hips", direction = Vector3.down });

        // Fallbacks
        AddIfMissing("brachiocephalic",   new VesselOntology { region = "upper_chest", direction = Vector3.up });
        AddIfMissing("celiac_trunk",      new VesselOntology { region = "spine",       direction = Vector3.down });
        AddIfMissing("hepatic_trunk",     new VesselOntology { region = "spine",       direction = Vector3.down });
    }

    /// <summary>
    /// Parse vessel name and return ontology (region, side, direction).
    /// Matching is case-insensitive and looks for known keywords.
    /// Side is inferred from suffix conventions: _R/_L, .r/.l, _right/_left, -r/-l, etc.
    /// </summary>
    public static VesselOntology Parse(string vesselName)
    {
        if (string.IsNullOrEmpty(vesselName))
            return new VesselOntology { region = "chest", side = "midline", direction = Vector3.down };

        string lower = vesselName.ToLowerInvariant();

        // determine side
        string side = "midline";
        if (lower.EndsWith("_r") || lower.EndsWith(".r") || lower.EndsWith("_right") || lower.EndsWith("-r"))
            side = "right";
        else if (lower.EndsWith("_l") || lower.EndsWith(".l") || lower.EndsWith("_left") || lower.EndsWith("-l"))
            side = "left";
        else if (lower.Contains("_r_") || lower.Contains("_right"))
            side = "right";
        else if (lower.Contains("_l_") || lower.Contains("_left"))
            side = "left";

        foreach (var kv in keywordOntology)
        {
            if (lower.Contains(kv.Key.ToLowerInvariant()))
            {
                var baseInfo = kv.Value;
                return new VesselOntology
                {
                    region    = baseInfo.region ?? "chest",
                    side      = side,
                    direction = baseInfo.direction
                };
            }
        }

        // default fallback
        return new VesselOntology
        {
            region    = "chest",
            side      = side,
            direction = Vector3.down
        };
    }
}
