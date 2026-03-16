
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

app.MapGet("/api/coa/{no}", async (string no, IAccurateService service) =>
{
    return await service.GetCoaDetail(no);
});

app.Run();
