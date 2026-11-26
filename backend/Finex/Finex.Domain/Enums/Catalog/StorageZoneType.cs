using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Domain.Enums.Catalog
{
    public enum StorageZoneType
    {
        AmbientDry = 1,             // Standard dry storage away from moisture and direct sunlight (dry tissues, diapers)
        TemperatureControlled = 2,  // Climate-controlled moderate temperature (wet wipes, disinfectants, natural oils)
        FoodGradeOdorFree = 3,      // Odor-isolated and food-grade compliant (table napkins, food-oil absorbent wipes)
        ChemicalIsolated = 4,       // Chemically isolated storage (chemical and grease absorbent pads/wipes)
        CleanroomDustFree = 5,      // Dust- and lint-isolated environment (lint-free wipes for laboratories)
        ElectronicsProtected = 6    // Shock- and moisture-protected storage (automatic and electronic dispensers)
    }

}
