var builder = DistributedApplication.CreateBuilder(args);
var api = builder.AddProject<Projects.CorporateTaxCategorization_Api>("categorization-api");
builder.AddJavaScriptApp("angular-client", "../CorporateTaxCategorization.Client")
    .WithRunScript("start")
    .WithHttpEndpoint(port: 4200, targetPort: 4200, env: "PORT", isProxied: false)
    .WithReference(api)
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api);
builder.Build().Run();
