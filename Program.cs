
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<AccurateHttpClient>();
builder.Services.AddScoped<IAccurateService, AccurateService>();

var app = builder.Build();

app.UseCors("AllowAll");

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "API Running");

app.MapGet("/api/database-host", async (IAccurateService service) =>
{
    return await service.GetDatabaseHost();
});

// Return raw JSON from Accurate so envelope { "s", "d" } and "balance" are preserved (no double-serialize).
app.MapGet("/api/coa/{no}", async (string no, IAccurateService service) =>
{
    try
    {
        var rawJson = await service.GetCoaDetailRaw(no);
        return Results.Content(rawJson, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
    }
});

app.Run();
