
using MyBackend.Application.Services;
using MyBackend.Features.Coa;
using MyBackend.Features.Companies;
using MyBackend.Features.LaporanKeuangan;
using MyBackend.Features.Root;
using MyBackend.Features.SalesOrders;
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

app.MapRootEndpoints();
app.MapCompaniesEndpoints();
app.MapCoaEndpoints();
app.MapLaporanKeuanganEndpoints();
app.MapSalesOrdersEndpoints();

app.Run();
