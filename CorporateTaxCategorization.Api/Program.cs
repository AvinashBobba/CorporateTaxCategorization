using CorporateTaxCategorization.Api.Services;
using CorporateTaxCategorization.MachineLearning;
using CorporateTaxCategorization.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(_ => new MiniLmTextEncoder(Path.Combine(AppContext.BaseDirectory, "Models", "all-MiniLM-L6-v2")));
builder.Services.AddSingleton<CategorizationService>();
builder.Services.AddDbContext<TaxDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("TaxDatabase")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:TaxDatabase.")));
builder.Services.AddSingleton<WorkbookStorage>();
builder.Services.AddScoped<WorkbookImporter>();
builder.Services.AddHostedService<WorkbookImportWorker>();
builder.Services.AddHostedService<PredictionWorker>();
var app = builder.Build();
await DevelopmentDatabaseStartup.InitializeAsync(app.Services, app.Configuration, app.Logger);
app.UseExceptionHandler();
app.MapGet("/", () => Results.Redirect("http://localhost:4200"));
app.MapControllers();
app.Run();

public partial class Program { }
