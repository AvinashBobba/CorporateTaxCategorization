using System.Text.Json;
using CorporateTaxCategorization.Api.Data;
using CorporateTaxCategorization.MachineLearning;
using Microsoft.EntityFrameworkCore;

namespace CorporateTaxCategorization.Api.Services;

public static class SyntheticCategories
{
    public const string ModelVersion = "all-MiniLM-L6-v2@10dbd2f/mean-l2-256/v1";
    // Synthetic taxonomy descriptions, not labels read from incoming Excel rows.
    private static readonly (string Type, string Category, string Text)[] Definitions =
    [
        ("DPL", "Utilities", "Electricity, gas and water supply bills for business premises"),
        ("DPL", "Insurance", "Annual business insurance premiums and liability cover"),
        ("DPL", "ProfessionalFees", "Accountancy, audit and legal professional service fees"),
        ("DPL", "Rent", "Monthly rent and lease payments for office premises"),
        ("DPL", "Travel", "Business travel, train tickets, air fares and hotel accommodation"),
        ("TFA", "ComputerEquipment", "Laptop computers, desktop PCs, servers and computer monitors"),
        ("TFA", "Furniture", "Office desks, chairs, filing cabinets and furniture"),
        ("TFA", "MotorVehicles", "Company cars, vans and delivery vehicles"),
        ("TFA", "PlantMachinery", "Manufacturing machinery, production equipment and industrial machines"),
        ("TFA", "FixturesFittings", "Installed shelving, lighting fixtures and building fittings"),
        ("CRD", "Capital", "Acquisition of a new lasting business asset, purchase of equipment or major building improvement"),
        ("CRD", "Revenue", "Recurring operating costs, routine repairs, subscriptions, cleaning, rent and maintenance"),
        ("CRD", "Mixed", "Combined invoice includes new equipment acquisition and recurring support service charges"),
        ("CRD", "ReviewRequired", "Unclear work description, general invoice, insufficient evidence to establish the nature of expenditure")
    ];

    public static async Task SeedAsync(TaxDbContext db, MiniLmTextEncoder encoder, string clientId, CancellationToken ct)
    {
        foreach (var item in Definitions)
        {
            if (await db.CategoryReferences.AnyAsync(x => x.ClientId == clientId && x.SheetType == item.Type &&
                x.Category == item.Category && x.ModelVersion == ModelVersion && x.IsSynthetic, ct)) continue;
            db.CategoryReferences.Add(new CategoryReference
            {
                ClientId = clientId, SheetType = item.Type, Category = item.Category,
                Description = item.Text, ModelVersion = ModelVersion, IsSynthetic = true,
                EmbeddingJson = JsonSerializer.Serialize(encoder.Encode(item.Text).Vector)
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
