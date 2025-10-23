using Core.Domain;
using Infrastructure.DataAccess;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

builder.Services.AddSingleton(new MySqlConnectionFactory(
    builder.Configuration.GetConnectionString("TransportApp")!
));

builder.Services.AddScoped<VoertuigRepository>();
builder.Services.AddScoped<RitRepository>();
builder.Services.AddScoped<TransportPlanner>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();