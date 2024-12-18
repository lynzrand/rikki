using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using Rynco.Rikki.Db;

var options = Parser.Default.ParseArguments<Options>(args).Value;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<RikkiDbContext>();
builder.Services.AddScoped<HighDb>();
builder.Services.AddLogging();
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseRouting();
app.UsePathBase("/api/v1");
app.MapControllers();

app.Run();

class Options
{
    [Option("port", Default = 5000)]
    public required int Port { get; set; }

    [Option("host", Default = "localhost")]
    public required string Host { get; set; }

    [Option("git-root", Default = "./data/git")]
    public required string GitRoot { get; set; }
}
