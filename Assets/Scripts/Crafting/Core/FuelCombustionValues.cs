using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Real-world combustion energy constants for biomass molecules.
/// Source: literature values (MJ/kg dry weight).
/// Used by SteamGenerator to calculate how many Joules a fuel item contains.
/// </summary>
public static class FuelCombustionValues
{
    // ── Dry combustion values (MJ/kg) ────────────────────────────────────────
    public const float CelluloseMJPerKg      = 17.3f;
    public const float HemicelluloseMJPerKg  = 18.0f;
    public const float LigninMJPerKg         = 27.2f;  // highest — aromatic benzene rings
    public const float CharcoalMJPerKg       = 30.0f;
    public const float BiomassCharMJPerKg    = 28.5f;  // char from fast pyrolysis (slightly lower than pure charcoal)
    public const float EthanolMJPerKg        = 26.8f;
    public const float FurfuralMJPerKg       = 24.3f;  // hemicellulose acid-hydrolysis product
    public const float BioOilMJPerKg         = 16.5f;  // fast-pyrolysis bio-oil (phenolics + water, ~20% O)
    public const float BiodieselHDOMJPerKg   = 44.5f;  // hydrodeoxygenated biodiesel (~fossil diesel quality)
    public const float BiodieselFAMEMJPerKg  = 38.5f;  // transesterification FAME biodiesel
    public const float SyngasMJPerKg         = 12.0f;  // compressed syngas (CO+H2 mix, game abstraction)
    public const float BiocrudeMJPerKg       = 33.0f;  // HTL biocrude (lower O than bio-oil)
    public const float GlycerolMJPerKg       = 16.0f;  // transesterification byproduct; burnable
    public const float MethanolMJPerKg       = 19.9f;  // methanol (reagent; also combustible)
    public const float LipidMJPerKg          = 37.0f;  // plant triglycerides (~FAME biodiesel quality)
    public const float WoodWaxesMJPerKg      = 40.0f;  // long-chain alkane waxes (like paraffin)
    public const float CuticularWaxesMJPerKg = 38.0f;  // leaf-surface waxes (C20-C34 alkanes)
    public const float SuberinMJPerKg        = 27.0f;  // cork-like polyester in bark

    // ── Water vaporization penalty (MJ per kg of water present) ─────────────
    // Every kg of water in the fuel absorbs 2.26 MJ just to evaporate before burning.
    public const float WaterVaporizationMJPerKg = 2.26f;

    // ── Moisture efficiency lookup ────────────────────────────────────────────
    // Below 40% MC items are burnable with these multipliers:
    //   >40 MC  → 0.30×  (barely burns, heavy steam penalty)
    //   25-40%  → 0.65×
    //   15-25%  → 1.00×  (air-dry baseline)
    //   8-12%   → 1.15×  (kiln-dry bonus)
    //   <8%     → 1.15×  (same, bone-dry doesn't gain more in practice)

    private static readonly Dictionary<string, float> _fuelValues =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Cellulose",        CelluloseMJPerKg      },
            { "Hemicellulose",    HemicelluloseMJPerKg  },
            { "Lignin",           LigninMJPerKg         },
            { "Charcoal",         CharcoalMJPerKg       },
            { "Carbon",           CharcoalMJPerKg       },   // charcoal/biochar carbon
            { "Biochar",          BiomassCharMJPerKg    },
            { "Ethanol",          EthanolMJPerKg        },
            { "Furfural",         FurfuralMJPerKg       },
            { "Bio-oil",          BioOilMJPerKg         },
            { "Biodiesel",        BiodieselFAMEMJPerKg  },
            { "Biodiesel (HDO)",  BiodieselHDOMJPerKg   },
            { "Syngas",           SyngasMJPerKg         },
            { "Biocrude",         BiocrudeMJPerKg       },
            { "Glycerol",         GlycerolMJPerKg       },
            { "Methanol",         MethanolMJPerKg       },
            { "Lipid",            LipidMJPerKg          },
            { "Wood Waxes",       WoodWaxesMJPerKg      },
            { "Cuticular Waxes",  CuticularWaxesMJPerKg },
            { "Suberin",          SuberinMJPerKg        },
        };

    /// <summary>
    /// Calculates effective MJ/kg for an item given its composition.
    /// Accounts for water vaporization penalty.  Returns 0 if not combustible.
    /// </summary>
    public static float CalculateEffectiveMJPerKg(IEnumerable<Composition> composition)
    {
        if (composition == null) return 0f;

        float fuelMJ        = 0f;
        float waterFraction = 0f;

        foreach (var comp in composition)
        {
            float frac = comp.percentage / 100f;
            if (_fuelValues.TryGetValue(comp.resource, out float mjPerKg))
                fuelMJ += frac * mjPerKg;
            else if (string.Equals(comp.resource, "Water", System.StringComparison.OrdinalIgnoreCase))
                waterFraction = frac;
        }

        // Water vaporization costs energy before combustion can proceed
        float penalty = waterFraction * WaterVaporizationMJPerKg;
        return Mathf.Max(0f, fuelMJ - penalty);
    }

    /// <summary>
    /// Converts mass in grams and effective MJ/kg to Joules.
    /// </summary>
    public static float MassGramsToJoules(float massGrams, float effectiveMJPerKg)
        => (massGrams / 1000f) * effectiveMJPerKg * 1_000_000f;

    /// <summary>
    /// Returns true if the composition contains any combustible molecule.
    /// </summary>
    public static bool IsCombustible(IEnumerable<Composition> composition)
        => CalculateEffectiveMJPerKg(composition) > 0f;
}
